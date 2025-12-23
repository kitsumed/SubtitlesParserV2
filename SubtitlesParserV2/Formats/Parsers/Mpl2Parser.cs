using SubtitlesParserV2.Helpers;
using SubtitlesParserV2.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace SubtitlesParserV2.Formats.Parsers
{
	/// <summary>
	/// <para>Parser for the .mpl subtitles files.</para>
	/// </summary>
	/// 
	/// <!--
	/// Sources:
	/// https://wiki.multimedia.cx/index.php/MPL2
	/// Example:
	/// [604][640]Sample 1
	/// [650][686]Sample 2!
	/// -->
	internal class Mpl2Parser : ISubtitlesParser
	{
		// Format [00][00] and separate by two group
		private static readonly Regex TimestampRegex = new Regex(@"\[(?<START>\d+)]\[(?<END>\d+)]", RegexOptions.Compiled);
		public List<SubtitleModel> ParseStream(Stream mpl2Stream, Encoding encoding)
		{
			StreamHelper.ThrowIfStreamIsNotSeekableOrReadable(mpl2Stream);
			// seek the beginning of the stream
			mpl2Stream.Position = 0;

			// Create a StreamReader & configure it to leave the main stream open when disposing
			using StreamReader reader = new StreamReader(mpl2Stream, encoding, true, 4096, true);

			List<SubtitleModel> items = new List<SubtitleModel>();

			string? currentLine = reader.ReadLine();
			// Loop until we reach end of file
			while (currentLine != null) 
			{
				(int lineStartms, int lineEndms) = ParseMpl2Timestamp(currentLine);
				List<string> lineContent = ParseMpl2Line(currentLine);
				items.Add(new SubtitleModel()
				{
					StartTime = lineStartms,
					EndTime = lineEndms,
					Lines = lineContent
				});
				currentLine = reader.ReadLine();
			}

			// Ensure we at least found 1 valid item
			if (items.Count >= 1)
			{
				return items;
			}
			else
			{
				throw new ArgumentException("Stream is not in a valid Mpl2 format");
			}
		}


		/// <summary>
		/// Parse the content of one Mpl2 line (assuming '|' new line character is used)
		/// </summary>
		/// <param name="line"></param>
		/// <returns>The lines content</returns>
		private static List<string> ParseMpl2Line(string line)
		{
			// Parse the line
			string[] parts = line.Split(']', 3);
			// Ensure there is at least 3 string ( [START] & [END] & CONTENT )
			if (parts.Length < 3) throw new ArgumentException("Stream line is not in a valid Mpl2 format.");

			// Two first elements are the timestamp, what follow it the content
			string content = parts[2];
			// Apply new line character
			return content.Split('|').Select(line => line.Trim()).ToList();
		}

		/// <summary>
		/// Parse the time of a Mpl2 line and convert it to milliseconds
		/// </summary>
		/// <!--
		/// Time Format: [SS][SS] (start time followed by end time in seconds)
		/// Example:
		/// [604][640]Sample 1
		/// [650][686]Sample 2!
		/// -->
		/// <param name="line"></param>
		/// <returns>The start and end time in milliseconds of the line</returns>
		/// <exception cref="ArgumentException">When line is not in a valid format</exception>
		private static (int startTime, int endTime) ParseMpl2Timestamp(string line) 
		{
			// Parse the timestamp
			Match matchs = TimestampRegex.Match(line);
			// Ensure there is at least 2 matches ( [START] & [END] )
			// NOTE: We could default to defining time to -1 when invalid, however due to the file having almost no unique feature,
			// a invalid timestamp is the best way to detect that the current stream is not in Mpl2 format and stop parsing early.
			if (matchs.Groups.Count < 2) throw new ArgumentException("Stream line is not in a valid Mpl2 format.");

			int startTime = 0;
			int endTime = 0;
			// Parse time, throw error if it fail
			if (!int.TryParse(matchs?.Groups["START"]?.Value, out startTime) || !int.TryParse(matchs?.Groups["END"]?.Value, out endTime))
			{
				throw new ArgumentException("Stream line has invalid characters at positions used for time. Stream is not a valid Mpl2 format.");
			}
			return ((int)new TimeSpan(0, 0, startTime).TotalMilliseconds, (int)new TimeSpan(0, 0, endTime).TotalMilliseconds);
		}
	}
}