using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using NLog;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Plugins;
using NzbDrone.Plugin.Qobuz.API;
using QobuzApiSharp.Models.Content;

namespace NzbDrone.Core.Download.Clients.Qobuz.Queue
{
    public class DownloadItem
    {
        public static async Task<DownloadItem> From(RemoteAlbum remoteAlbum)
        {
            var url = remoteAlbum.Release.DownloadUrl.Trim();
            var quality = remoteAlbum.Release.Container switch
            {
                "320" => AudioQuality.MP3320,
                "Lossless" => AudioQuality.FLACLossless,
                "24bit 96kHz" => AudioQuality.FLACHiRes24Bit96kHz,
                "24bit 192kHz" => AudioQuality.FLACHiRes24Bit192Khz,
                _ => AudioQuality.MP3320,
            };

            DownloadItem item = null;
            if (url.Contains("qobuz", StringComparison.CurrentCultureIgnoreCase))
            {
                if (QobuzURL.TryParse(url, out var qobuzUrl))
                {
                    item = new()
                    {
                        ID = Guid.NewGuid().ToString(),
                        Status = DownloadItemStatus.Queued,
                        Bitrate = quality,
                        RemoteAlbum = remoteAlbum,
                        _qobuzUrl = qobuzUrl,
                    };

                    await item.SetQobuzData();
                }
            }

            return item;
        }

        public string ID { get; private set; }

        public string Title { get; private set; }
        public string Artist { get; private set; }
        public bool Explicit { get; private set; }

        public RemoteAlbum RemoteAlbum {  get; private set; }

        public string DownloadFolder { get; private set; }

        public AudioQuality Bitrate { get; private set; }
        public DownloadItemStatus Status { get; set; }

        public float Progress { get => DownloadedSize / (float)Math.Max(TotalSize, 1); }
        public long DownloadedSize { get; private set; }
        public long TotalSize { get; private set; }

        public int FailedTracks { get; private set; }

        private string[] _tracks;
        private QobuzURL _qobuzUrl;
        private Album _qobuzAlbum;

        public async Task DoDownload(QobuzSettings settings, Logger logger, CancellationToken cancellation = default)
        {
            List<Task> tasks = new();
            using SemaphoreSlim semaphore = new(3, 3);
            foreach (var trackId in _tracks)
            {
                tasks.Add(Task.Run(async () =>
                {
                    await semaphore.WaitAsync(cancellation);
                    try
                    {
                        await DoTrackDownload(trackId, settings, cancellation);
                        DownloadedSize++;
                    }
                    catch (TaskCanceledException) { }
                    catch (Exception ex)
                    {
                        logger.Error("Error while downloading Qobuz track " + trackId);
                        logger.Error(ex.ToString());
                        FailedTracks++;
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                }, cancellation));
            }

            await Task.WhenAll(tasks);
            if (FailedTracks > 0)
                Status = DownloadItemStatus.Failed;
            else
                Status = DownloadItemStatus.Completed;
        }

        private async Task DoTrackDownload(string track, QobuzSettings settings, CancellationToken cancellation = default)
        {
            var page = QobuzAPI.Instance.Client.GetTrack(track, true);
            var songTitle = page.CompleteTitle;
            var artistName = page.Performer.Name;
            var albumTitle = page.Album.CompleteTitle;
            var duration = page.Duration;

            var ext = Bitrate == AudioQuality.MP3320 ? "mp3" : "flac";
            var outPath = Path.Combine(settings.DownloadPath, MetadataUtilities.GetFilledTemplate("%albumartist%/%album%/", ext, page, _qobuzAlbum), MetadataUtilities.GetFilledTemplate("%volume% - %track% - %title%.%ext%", ext, page, _qobuzAlbum));
            var outDir = Path.GetDirectoryName(outPath)!;

            DownloadFolder = outDir;
            if (!Directory.Exists(outDir))
                Directory.CreateDirectory(outDir);

            await QobuzAPI.Instance.Client.WriteRawTrackToFile(track, outPath, Bitrate, cancellation);

            var plainLyrics = string.Empty;
            string syncLyrics = null;

            if (settings.UseLRCLIB && (string.IsNullOrWhiteSpace(plainLyrics) || (settings.SaveSyncedLyrics && !(syncLyrics?.Any() ?? false))))
            {
                var lyrics = await Downloader.FetchLyricsFromLRCLIB("lrclib.net", songTitle, artistName, albumTitle, duration ?? 0, cancellation);
                if (lyrics != null)
                {
                    if (string.IsNullOrWhiteSpace(plainLyrics))
                        plainLyrics = lyrics.Value.plainLyrics;
                    if (settings.SaveSyncedLyrics && !(syncLyrics?.Any() ?? false))
                        syncLyrics = lyrics.Value.syncLyrics;
                }
            }

            await QobuzAPI.Instance.Client.ApplyMetadataToFile(track, outPath, plainLyrics, token: cancellation);

            if (!string.IsNullOrWhiteSpace(syncLyrics))
                await CreateLrcFile(Path.Combine(outDir, MetadataUtilities.GetFilledTemplate("%volume% - %track% - %title%.%ext%", "lrc", page, _qobuzAlbum)), syncLyrics);

            // TODO: this is currently a waste of resources, if this pr ever gets merged, it can be reenabled
            // https://github.com/Lidarr/Lidarr/pull/4370
            /* try
            {
                string artOut = Path.Combine(outDir, "folder.jpg");
                if (!File.Exists(artOut))
                {
                    byte[] bigArt = await QobuzAPI.Instance.Client.Downloader.GetArtBytes(page["DATA"]!["ALB_PICTURE"]!.ToString(), 1024, cancellation);
                    await File.WriteAllBytesAsync(artOut, bigArt, cancellation);
                }
            }
            catch (UnavailableArtException) { } */
        }

        private async Task SetQobuzData(CancellationToken cancellation = default)
        {
            if (_qobuzUrl.EntityType != EntityType.Album)
                throw new InvalidOperationException();

            var album = QobuzAPI.Instance.Client.GetAlbum(_qobuzUrl.Id, true);
            _tracks ??= album.Tracks.Items.Select(t => t.Id.ToString()).ToArray();

            _qobuzAlbum = album;

            Title = album.CompleteTitle;
            Artist = album.Artist.Name;
            Explicit = album.ParentalWarning.GetValueOrDefault();
            TotalSize = _tracks.Length;
        }

        private static async Task CreateLrcFile(string lrcFilePath, string syncLyrics)
        {
            await File.WriteAllTextAsync(lrcFilePath, syncLyrics);
        }
    }
}
