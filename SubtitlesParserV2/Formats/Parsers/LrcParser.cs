using SubtitlesParserV2.Helpers;
using SubtitlesParserV2.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace SubtitlesParserV2.Formats.Parsers
{
	/// <summary>
	/// Configuration model for the Lrc parser.
	/// </summary>
	public class LrcParserConfig
	{
		/// <summary>
		/// Define the maximum number of lines the program will continue reading before exiting if it
		/// haven't found any lines in Lrc format. Default is 20, which is usally more than enought to find the first line actual subtitle line.
		/// </summary>
		public int FirstLineSearchTimeout { get; set; } = 20;
	}

	internal class LrcParser : ISubtitlesParser, ISubtitlesParser<LrcParserConfig>
	{
		// Format : [0000:00.00] / [mm:ss.cc] where cc is centiseconds (hundredths of a second)
		private static readonly Regex ShortTimestampCentisecondsRegex = new Regex(@"\[(?<M>\d+):(?<S>\d{2})\.(?<C>\d{2})\]", RegexOptions.Compiled);
        // Format : [0000:00.000] / [mm:ss.mmm] where mmm is milliseconds (Not part of the official docs, but some applications decided to do it that way)
        private static readonly Regex ShortTimestampMillisecondsRegex = new Regex(@"\[(?<M>\d+):(?<S>\d{2})\.(?<MS>\d{3})\]", RegexOptions.Compiled);
		// Format : [0000:00:00.00] / [hh:mm:ss.cc] where cc is centiseconds
		private static readonly Regex LongTimestampCentisecondsRegex = new Regex(@"\[(?<H>\d+):(?<M>\d+):(?<S>\d{2})\.(?<C>\d{2})\]", RegexOptions.Compiled);
        // Format : [0000:00:00.000] / [hh:mm:ss.mmm] where mmm is milliseconds (Not part of the official docs, but some applications decided to do it that way)
        private static readonly Regex LongTimestampMillisecondsRegex = new Regex(@"\[(?<H>\d+):(?<M>\d+):(?<S>\d{2})\.(?<MS>\d{3})\]", RegexOptions.Compiled);
		// Format <00:00.00> or <00:00.000> used inside the lines by the Enhanced LRC format (A2 Extension)
		private static readonly Regex EnhancedLrcFormatRegex = new Regex(@"<\d{2}:\d{2}\.\d{2,3}>", RegexOptions.Compiled);


		public List<SubtitleModel> ParseStream(Stream lrcStream, Encoding encoding)
		{
			return ParseStream(lrcStream, encoding, new LrcParserConfig());
		}

		/// <summary>
		/// <para>Parser for the .lrc subtitles files.</para>
		/// <para><strong>Support</strong> : Core LRC, Enhanced LRC format (A2 extension).
		/// <strong>NOTE</strong>: Last item end time will always be -1
		/// </para>
		/// </summary>
		/// 
		/// <!--
		/// Sources:
		/// https://en.wikipedia.org/wiki/LRC_(file_format)
		/// https://docs.fileformat.com/misc/lrc/
		/// Example:
		/// [ar:Artist performing]
		/// [al: Album name]
		/// [ti: Media title]
		/// [au: Artist name]
		/// [length: 0:40]
		/// # This is a comment, line 6 uses Enhanced LRC format
		/// 
		/// [00:12.00] Line 1 lyrics (centiseconds)
		/// [00:17.20] Line 2 lyrics (centiseconds)
		/// [00:21.100] Line 3 lyrics (milliseconds)
		/// [00:24.000] Line 4 lyrics (milliseconds)
		/// [00:28.25] Line 5 lyrics (centiseconds)
		/// [00:29.02] Line 6 <00:34.20>lyrics (Enhanced LRC format)
		/// [00:39.00] last lyrics.
		/// -->
		public List<SubtitleModel> ParseStream(Stream lrcStream, Encoding encoding, LrcParserConfig configuration)
		{
			// Ensure the stream is seekable and readable
			StreamHelper.ThrowIfStreamIsNotSeekableOrReadable(lrcStream);
			lrcStream.Position = 0;

			using StreamReader reader = new StreamReader(lrcStream, encoding, true, 4096, true);
			List<SubtitleModel> items = new List<SubtitleModel>();

			string? lastLine = null;
			string? currentLine = null;

			// Read the file line by line
			while ((currentLine = reader.ReadLine()) != null)
			{
				// Validate the current line format early
				if (!IsValidLrcLine(currentLine))
				{
					configuration.FirstLineSearchTimeout--;
					// We didn't find the first valid line and reached the search timeout
					if (configuration.FirstLineSearchTimeout <= 0) throw new ArgumentException("Stream is not in a valid Lrc format (could not find valid timestamp format inside given line timeout)");
				}

				// Process the previous line now that we know it's end time with the current line
				if (lastLine != null)
				{
					int? startTime = ParseLrcTime(lastLine);
					int? endTime = ParseLrcTime(currentLine);

					if (startTime.HasValue && endTime.HasValue)
					{
						items.Add(new SubtitleModel
						{
							StartTime = startTime.Value,
							EndTime = endTime.Value,
							Lines = new List<string> { CleanLrcLine(lastLine) }
						});
					}
				}

				lastLine = currentLine;
			}

			// Process the last line if it exists
			if (lastLine != null)
			{
				int startTime = ParseLrcTime(lastLine);

				items.Add(new SubtitleModel
				{
					StartTime = startTime,
					EndTime = -1, // We can't know the end time of the last line of our file
					Lines = new List<string> { CleanLrcLine(lastLine) }
				});
			}

			// Verify we have parsed at least 1 subtitle
			if (items.Count == 0) throw new ArgumentException("Stream is not in a valid Lrc format");
			return items;
		}

		/// <summary>
		/// Checks if a line contains a valid LRC timestamp.
		/// </summary>
		/// <returns>True if the line is valid, if not, false.</returns>
		/// <param name="line">The line to check.</param>
		private static bool IsValidLrcLine(string line)
		{
			return ShortTimestampCentisecondsRegex.IsMatch(line) || 
			       ShortTimestampMillisecondsRegex.IsMatch(line) || 
			       LongTimestampCentisecondsRegex.IsMatch(line) || 
			       LongTimestampMillisecondsRegex.IsMatch(line);
		}

		/// <summary>
		/// Removes timestamps and enhanced LRC format tags from a line.
		/// </summary>
		/// <returns>The cleaned line without timestamps.</returns>
		/// <param name="line">The line to clean.</param>
		private static string CleanLrcLine(string line)
		{
			int timestampEndIndex = line.IndexOf(']') + 1;
			string content = line.Substring(timestampEndIndex).Trim();
			return EnhancedLrcFormatRegex.Replace(content, string.Empty);
		}

		/// <summary>
		/// Parses the timestamp from a line and converts it to milliseconds.
		/// Supports both centiseconds (2 digits: .XX) and milliseconds (3 digits: .XXX) formats.
		/// Example:
		/// [00:00.50] My lyrics! (centiseconds)
		/// [00:00.500] My lyrics! (milliseconds)
		/// </summary>
		/// <returns>The timestamp in milliseconds or -1 if it could not be parsed.</returns>
		/// <param name="line">The line containing the timestamp.</param>
		private static int ParseLrcTime(string line)
		{
			Match match;
			bool isMilliseconds = false;

			// Try short format with milliseconds first (3 digits)
			match = ShortTimestampMillisecondsRegex.Match(line);
			if (match.Success)
			{
				isMilliseconds = true;
			}
			else
			{
				// Try short format with centiseconds (2 digits)
				match = ShortTimestampCentisecondsRegex.Match(line);
				if (!match.Success)
				{
					// Try long format with milliseconds (3 digits)
					match = LongTimestampMillisecondsRegex.Match(line);
					if (match.Success)
					{
						isMilliseconds = true;
					}
					else
					{
						// Try long format with centiseconds (2 digits)
						match = LongTimestampCentisecondsRegex.Match(line);
					}
				}
			}

			if (match.Success)
			{
				int hours = int.TryParse(match.Groups["H"]?.Value, out int h) ? h : 0;
				int minutes = int.TryParse(match.Groups["M"]?.Value, out int m) ? m : 0;
				int seconds = int.TryParse(match.Groups["S"]?.Value, out int s) ? s : 0;
				
				int milliseconds;
				if (isMilliseconds)
				{
					// Parse milliseconds directly (3 digits)
					milliseconds = int.TryParse(match.Groups["MS"]?.Value, out int ms) ? ms : 0;
				}
				else
				{
					// Parse centiseconds (2 digits) and convert to milliseconds
					int centiseconds = int.TryParse(match.Groups["C"]?.Value, out int c) ? c : 0;
					milliseconds = centiseconds * 10;
				}

				// Convert to total milliseconds
				return (int)new TimeSpan(0, hours, minutes, seconds, milliseconds).TotalMilliseconds;
			}

			return -1;
		}
	}
}