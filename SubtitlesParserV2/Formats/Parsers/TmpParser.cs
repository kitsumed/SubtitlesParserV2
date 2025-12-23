using SubtitlesParserV2.Helpers;
using SubtitlesParserV2.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace SubtitlesParserV2.Formats.Parsers
{
	/// <summary>
	/// <para>Parser for the .tmp subtitles files.</para>
	/// <strong>NOTE</strong>: Last item end time will always be -1
	/// </summary>
	/// 
	/// <!--
	/// Sources:
	/// https://web.archive.org/web/20080121210347/https://napisy.ovh.org/readarticle.php?article_id=4
	/// https://wiki.multimedia.cx/index.php/TMPlayer
	/// Example:
	/// 00:01:52:Sample 1
	/// 00:01:55:Sample 2!
	/// -->
	internal class TmpParser : ISubtitlesParser
	{
		public List<SubtitleModel> ParseStream(Stream tmpStream, Encoding encoding)
		{
			StreamHelper.ThrowIfStreamIsNotSeekableOrReadable(tmpStream);
			// seek the beginning of the stream
			tmpStream.Position = 0;

			// Create a StreamReader & configure it to leave the main stream open when disposing
			using StreamReader reader = new StreamReader(tmpStream, encoding, true, 4096, true);

			List<SubtitleModel> items = new List<SubtitleModel>();

			// Store a line as the lastLine so we can re-use it once we know the next line
			// (Since the nextLine start time is also the end time for the lastLine)
			string lastLine = reader.ReadLine() ?? throw new ArgumentException("Stream reached end of file on first reading attempt.");
			// Loop until last line was processed (is null), then do a final loop
			do
			{
				string? nextLine = reader.ReadLine();
				// Parse last line
				(int lastLineTimeMs, List<string> lastLinesContent) = ParseTmpLine(lastLine);

				// If nextLine exists, we can know the end time of the previous line
				if (nextLine != null)
				{
					// Parse current line (Aka, end time of lastLine)
					(int nextLineTimeMs, _) = ParseTmpLine(nextLine);
					items.Add(new SubtitleModel()
					{
						StartTime = lastLineTimeMs,
						EndTime = nextLineTimeMs,
						Lines = lastLinesContent
					});
				}
				else if (nextLine == null) // If we reached the end of the file, there is only "lastLine" that need to be added to items
				{
					items.Add(new SubtitleModel()
					{
						StartTime = lastLineTimeMs,
						EndTime = -1, // Since this is the last item, we can't know the end time, we could implement support for files that mention the "length".
						Lines = lastLinesContent
					});
					break; // Once we reach that point, end of file was reached
				}
				lastLine = nextLine; // Put our current line into the lastLine before starting the loop again
			} while (lastLine != null);

			// Ensure we at least found 1 valid item
			if (items.Count >= 1)
			{
				return items;
			}
			else
			{
				throw new ArgumentException("Stream is not in a valid Tmp format");
			}
		}


		/// <summary>
		/// Parse one Tmp format line to get the time in milliseconds and the lines content (assuming '|' new line character is used)
		/// </summary>
		/// <!--
		/// Time Format: HH:MM:SS
		/// Example:
		/// 00:00:00:My lyrics!
		/// 00:00:02:My first line!|Second line!
		/// -->
		/// <param name="line"></param>
		/// <returns>The time in milliseconds and the lines content</returns>
		/// <exception cref="ArgumentException">When line is not in a valid format</exception>
		private static (int time, List<string> linesContent) ParseTmpLine(string line)
		{
			// Only split the first 4 ':', after which everything else is part of index 3 (Aka, the content)
			string[] parts = line.Split(':', 4);
			// Ensure they is at least 4 separations on the line
			// NOTE: We could default to defining time to -1 when invalid, however due to the file having almost no unique feature,
			// a invalid timestamp is the best way to detect that the current stream is not in TMP format and stop parsing early
			if (parts.Length < 4) throw new ArgumentException("Stream line is not in a valid TMP format.");

			int hours = 0;
			int minutes = 0;
			int seconds = 0;
			// Parse time, throw error if it fail
			if (!int.TryParse(parts[0], out hours) || !int.TryParse(parts[1], out minutes) || !int.TryParse(parts[2], out seconds)) 
			{
				throw new ArgumentException("Stream line has invalid characters at positions used for time. Stream is not a valid TMP format.");
			}
			// Return time in MS along with line content
			return ((int)new TimeSpan(hours, minutes, seconds).TotalMilliseconds, parts[3].Split('|').Select(line => line.Trim()).ToList());
		}
	}
}