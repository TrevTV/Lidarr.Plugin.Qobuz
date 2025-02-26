using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;
using QobuzApiSharp.Models.Content;
using static Npgsql.Replication.PgOutput.Messages.RelationMessage;

namespace NzbDrone.Core.Download.Clients.Qobuz
{
    internal static class MetadataUtilities
    {
        public static string GetFilledTemplate(string template, string ext, Track qobuzPage, Album qobuzAlbum)
        {
            var releaseDate = qobuzAlbum.ReleaseDateOriginal.GetValueOrDefault().DateTime;
            return GetFilledTemplate_Internal(template,
                qobuzPage.CompleteTitle,
                qobuzPage.Album.CompleteTitle,
                qobuzAlbum.Artist.Name,
                qobuzPage.Performer.Name,
                qobuzAlbum.Artists.Select(a => a.Name).ToArray(),
                [qobuzPage.Performer.Name], // tracks don't seem to have a proper way to handle multiple artists
                $"{qobuzPage.TrackNumber:00}",
                qobuzAlbum.TracksCount.ToString(),
                $"{qobuzPage.MediaNumber:00}",
                qobuzAlbum.MediaCount.ToString(),
                releaseDate.Year.ToString(CultureInfo.InvariantCulture),
                ext);
        }

        private static string GetFilledTemplate_Internal(string template, string title, string album, string albumArtist, string artist, string[] albumArtists, string[] artists, string track, string trackCount, string volume, string volumeCount, string year, string ext)
        {
            StringBuilder t = new(template);
            ReplaceC("%title%", title);
            ReplaceC("%album%", album);
            ReplaceC("%albumartist%", albumArtist);
            ReplaceC("%artist%", artist);
            ReplaceC("%albumartists%", string.Join("; ", albumArtists));
            ReplaceC("%artists%", string.Join("; ", artists));
            ReplaceC("%track%", track);
            ReplaceC("%trackcount%", trackCount);
            ReplaceC("%volume%", volume);
            ReplaceC("%volumecount%", volumeCount);
            ReplaceC("%ext%", ext);
            ReplaceC("%year%", year);

            return t.ToString();

            void ReplaceC(string o, string r)
            {
                t.Replace(o, CleanPath(r));
            }
        }

        public static string CleanPath(string str)
        {
            var invalid = Path.GetInvalidFileNameChars();
            for (var i = 0; i < invalid.Length; i++)
            {
                var c = invalid[i];
                str = str.Replace(c, '_');
            }
            return str;
        }
    }
}
