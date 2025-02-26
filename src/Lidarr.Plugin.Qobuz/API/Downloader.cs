using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json;
using NzbDrone.Core.Parser;
using QobuzApiSharp.Service;
using QobuzApiSharp.Models.Content;

namespace NzbDrone.Plugin.Qobuz.API;

public static class Downloader
{
    private static readonly byte[] FLAC_MAGIC = "fLaC"u8.ToArray();
    private static readonly HttpClient _client = new();

    public static async Task<Stream> GetRawTrackStream(this QobuzApiService s, string trackId, AudioQuality bitrate, CancellationToken token = default)
    {
        return await s.GetTrackData(trackId, bitrate, token);
    }

    public static async Task<byte[]> GetRawTrackBytes(this QobuzApiService s, string trackId, AudioQuality bitrate, CancellationToken token = default)
    {
        using MemoryStream stream = (MemoryStream)await s.GetRawTrackStream(trackId, bitrate, token);
        return stream.ToArray();
    }

    public static async Task WriteRawTrackToFile(this QobuzApiService s, string trackId, string trackPath, AudioQuality bitrate, CancellationToken token = default)
    {
        using FileStream fileStream = File.Open(trackPath, FileMode.Create);
        var stream = await s.GetTrackData(trackId, bitrate, token);
        stream.CopyTo(fileStream);

        stream.Dispose();
    }

    public static async Task ApplyMetadataToTrackStream(this QobuzApiService s, string trackId, Stream trackStream, string lyrics = "", CancellationToken token = default)
    {
        byte[] magicBuffer = new byte[4];
        await trackStream.ReadAsync(magicBuffer.AsMemory(0, 4), token);
        string ext = Enumerable.SequenceEqual(magicBuffer, FLAC_MAGIC) ? ".flac" : ".mp3";

        trackStream.Seek(0, SeekOrigin.Begin);

        StreamAbstraction abstraction = new("track" + ext, trackStream);
        using TagLib.File file = TagLib.File.Create(abstraction);
        await s.ApplyMetadataToTagLibFile(file, trackId, lyrics, token);

        trackStream.Seek(0, SeekOrigin.Begin);
    }

    public static async Task<byte[]> ApplyMetadataToTrackBytes(this QobuzApiService s, string trackId, byte[] trackData, string lyrics = "", CancellationToken token = default)
    {
        string ext = Enumerable.SequenceEqual(trackData[0..4], FLAC_MAGIC) ? ".flac" : ".mp3";

        FileBytesAbstraction abstraction = new("track" + ext, trackData);
        using TagLib.File file = TagLib.File.Create(abstraction);
        await s.ApplyMetadataToTagLibFile(file, trackId, lyrics, token);

        byte[] finalData = abstraction.MemoryStream.ToArray();
        await abstraction.MemoryStream.DisposeAsync();
        return finalData;
    }

    public static async Task ApplyMetadataToFile(this QobuzApiService s, string trackId, string trackPath, string lyrics = "", CancellationToken token = default)
    {
        using TagLib.File file = TagLib.File.Create(trackPath);
        await s.ApplyMetadataToTagLibFile(file, trackId, lyrics, token);
    }

    public static async Task<(string? plainLyrics, string? syncLyrics)?> FetchLyricsFromLRCLIB(string instance, string trackName, string artistName, string albumName, long duration, CancellationToken token = default)
    {
        var requestUrl = $"https://{instance}/api/get?artist_name={Uri.EscapeDataString(artistName)}&track_name={Uri.EscapeDataString(trackName)}&album_name={Uri.EscapeDataString(albumName)}&duration={duration}";
        var response = await _client.GetAsync(requestUrl, token);

        if (response.IsSuccessStatusCode)
        {
            var content = await response.Content.ReadAsStringAsync(token);
            var json = JObject.Parse(content);
            return (json["plainLyrics"]?.ToString(), json["syncedLyrics"]?.ToString());
        }

        return null;
    }

    public static async Task<byte[]> GetAlbumArtBytes(this QobuzApiService s, Album albumData, CancellationToken token = default)
    {
        using HttpRequestMessage message = new(HttpMethod.Get, albumData.Image.Large);
        HttpResponseMessage response = await _client.SendAsync(message, token);

        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            throw new Exception($"The art for {albumData.Id} is unavailable.");
        }

        return await response.Content.ReadAsByteArrayAsync(token);
    }

    private static async Task<Stream> GetTrackData(this QobuzApiService s, string trackId, AudioQuality bitrate, CancellationToken token = default)
    {
        var urls = (s.GetTrackFileUrl(trackId, ((int)bitrate).ToString())) ?? throw new Exception($"Track ID {trackId} has no available media sources for bitrate {bitrate}.");
        if (urls.Sample ?? false)
            throw new Exception("Qobuz provided a sample. The user probably does not have access to this quality of track.");

        HttpRequestMessage message = new(HttpMethod.Get, urls.Url);
        HttpResponseMessage response = await _client.SendAsync(message, token);
        Stream stream = await response.Content.ReadAsStreamAsync(token);

        return stream;
    }

    private static async Task ApplyMetadataToTagLibFile(this QobuzApiService s, TagLib.File track, string trackId, string lyrics = "", CancellationToken token = default)
    {
        var page = s.GetTrack(trackId, true);
        var albumPage = s.GetAlbum(page.Album.Id, true);

        byte[]? albumArt = null;
        try { albumArt = await s.GetAlbumArtBytes(albumPage, token); } catch (Exception) { }

        track.Tag.Title = page.CompleteTitle;
        track.Tag.Album = albumPage.CompleteTitle;
        track.Tag.Performers = [page.Performer.Name];
        track.Tag.AlbumArtists = albumPage.Artists.Select(x => x.Name).ToArray();
        DateTime releaseDate = page.ReleaseDateOriginal.GetValueOrDefault().DateTime;
        track.Tag.Year = (uint)releaseDate.Year;
        track.Tag.Track = (uint)page.TrackNumber;
        track.Tag.TrackCount = (uint)albumPage.TracksCount;
        track.Tag.Disc = (uint)page.MediaNumber;
        track.Tag.DiscCount = (uint)albumPage.MediaCount;
        if (albumPage.Genre != null && !string.IsNullOrEmpty(albumPage.Genre.Name))
            track.Tag.Genres = [ albumPage.Genre.Name ];

        if (albumArt != null)
            track.Tag.Pictures = [new TagLib.Picture(new TagLib.ByteVector(albumArt))];

        track.Tag.Lyrics = lyrics;

        track.Save();
    }
}

// https://stackoverflow.com/questions/14959320/taglib-sharp-file-from-bytearray-stream#31032997
internal class FileBytesAbstraction : TagLib.File.IFileAbstraction
{
    public FileBytesAbstraction(string name, byte[] bytes)
    {
        Name = name;

        MemoryStream stream = new();
        stream.Write(bytes, 0, bytes.Length);

        MemoryStream = stream;
    }

    public void CloseStream(Stream stream)
    {
        // shared read/write stream so we don't want it to close it when switching AccessMode (see TagLib.NonContainer.File.AccessMode for more context)
    }

    public string Name { get; private set; }

    public Stream ReadStream { get => MemoryStream; }

    public Stream WriteStream { get => MemoryStream; }

    public MemoryStream MemoryStream { get; private set; }
}

internal class StreamAbstraction : TagLib.File.IFileAbstraction
{
    public StreamAbstraction(string name, Stream stream)
    {
        Name = name;
        Stream = stream;
    }

    public void CloseStream(Stream stream)
    {
        // shared read/write stream so we don't want it to close it when switching AccessMode (see TagLib.NonContainer.File.AccessMode for more context)
    }

    public string Name { get; private set; }

    public Stream ReadStream { get => Stream; }

    public Stream WriteStream { get => Stream; }

    public Stream Stream { get; private set; }
}
