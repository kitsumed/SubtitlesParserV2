using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using SubtitlesParserV2.Helpers;
using SubtitlesParserV2.Models;

namespace SubtitlesParserV2.Formats.Parsers
{
	/// <summary>
	/// Parser for the .srt subtitles files
	/// </summary>
	/// <!--
	/// Sources:
	/// https://en.wikipedia.org/wiki/SubRip
	/// https://docs.fileformat.com/video/srt/
	/// Example:
	/// 1
	/// 00:00:10,500 --> 00:00:13,000
	/// Elephant's Dream
	///
	/// 2
	/// 00:00:15,000 --> 00:00:18,000
	/// At the left we can see...[12]
	/// --> 
	internal class SrtParser : ISubtitlesParser
	{

		// Properties -----------------------------------------------------------------------

		private static readonly string[] _delimiters = { "-->", "- >", "->" };
		private static readonly string[] _newLineCharacters = { "\r\n", "\r", "\n" };

		// Methods -------------------------------------------------------------------------

		public List<SubtitleModel> ParseStream(Stream srtStream, Encoding encoding)
		{
			StreamHelper.ThrowIfStreamIsNotSeekableOrReadable(srtStream);
			// seek the beginning of the stream
			srtStream.Position = 0;

			// Create a StreamReader & configure it to leave the main stream open when disposing
			using StreamReader reader = new StreamReader(srtStream, encoding, true, 1024, true);

			List<SubtitleModel> items = new List<SubtitleModel>();
			IEnumerable<string> srtSubParts = GetSrtSubTitleParts(reader).Peekable(out var srtSubPartsAny); // This is a lazy list, not yet into memory
			if (srtSubPartsAny) // Ensure at least 1 part was found
			{
				bool isFirstPart = true;
				foreach (string part in srtSubParts)
				{
					// Ensure that our stream does not have the WebVTT header in the first part, it has some similarities with SRT
					// and can somtimes work*, but time parsing will fail. https://www.w3.org/TR/webvtt1/#file-structure
					if (isFirstPart && part.Equals("WEBVTT", StringComparison.InvariantCultureIgnoreCase))
					{
						throw new FormatException("This stream seems to be in WebVTT format, Srt cannot parse it.");
					}

					// Split new lines
					IEnumerable<string> lines = part.Split(_newLineCharacters, StringSplitOptions.RemoveEmptyEntries)
						.Select(line => line.Trim());

					SubtitleModel item = new SubtitleModel();
					foreach (string line in lines)
					{
						// Verify if we already found the subtitle time or not
						if (item.StartTime == 0 && item.EndTime == 0)
						{
							// Try to get the timecode
							bool success = TryParseTimecodeLine(line, out int startTc, out int endTc);
							if (success)
							{
								item.StartTime = startTc;
								item.EndTime = endTc;
							}
						}
						else // We already found the subtitle time
						{
							// Strip formatting by removing anything within curly braces or angle brackets, which is how SRT styles text according to wikipedia (https://en.wikipedia.org/wiki/SubRip#Formatting)
							item.Lines.Add(Regex.Replace(line, @"\{.*?\}|<.*?>", string.Empty).Trim());
						}
					}

					// Ensure the subtitle item is valid before pushing it to the final list
					if ((item.StartTime != 0 || item.EndTime != 0) && item.Lines.Any())
					{
						items.Add(item);
					}

				}

				// Verify if we have found at least 1 subtitle, else throw
				if (items.Count != 0)
				{
					return items;
				}
				else
				{
					throw new ArgumentException("Stream is not in a valid Srt format");
				}
			}
			else
			{
				throw new FormatException("Parsing as srt returned no srt part.");
			}
		}

		/// <summary>
		/// Enumerates the subtitle parts in a srt file based on the standard line break observed between them. 
		/// A srt subtitle part is in the form:
		/// <code>
		/// 1
		/// 00:00:20,000 --> 00:00:24,400
		/// Altocumulus clouds occur between six thousand
		/// </code>
		/// </summary>
		/// <param name="reader">The textreader associated with the srt file</param>
		/// <returns>An IEnumerable(string) object containing all the subtitle parts</returns>
		private static IEnumerable<string> GetSrtSubTitleParts(TextReader reader)
		{
			string? line;
			StringBuilder stringBuilder = new StringBuilder();

			while ((line = reader.ReadLine()) != null)
			{
				if (string.IsNullOrEmpty(line.Trim()))
				{
					// return only if not empty
					string res = stringBuilder.ToString().TrimEnd();
					if (!string.IsNullOrEmpty(res))
					{
						yield return res;
					}
					stringBuilder = new StringBuilder();
				}
				else
				{
					stringBuilder.AppendLine(line);
				}
			}

			if (stringBuilder.Length > 0)
			{
				yield return stringBuilder.ToString();
			}
		}

		private static bool TryParseTimecodeLine(string line, out int startTc, out int endTc)
		{
			string[] parts = line.Split(_delimiters, StringSplitOptions.None);
			if (parts.Length != 2)
			{
				// this is not a timecode line
				startTc = -1;
				endTc = -1;
				return false;
			}
			else
			{
				startTc = ParseSrtTimecode(parts[0]);
				endTc = ParseSrtTimecode(parts[1]);
				return true;
			}
		}

		/// <summary>
		/// Takes an SRT timecode as a string and parses it into a milliseconds. A SRT timecode reads as follows: 
		/// 00:00:20,000
		/// </summary>
		/// <param name="timecode">The timecode to parse</param>
		/// <returns>The parsed timecode in milliseconds. If the parsing was unsuccessful, -1 is returned</returns>
		private static int ParseSrtTimecode(string timecode)
		{
			Match match = Regex.Match(timecode, "[0-9]+:[0-9]+:[0-9]+([,\\.][0-9]+)?");
			if (match.Success)
			{
				timecode = match.Value;
				TimeSpan result;
				if (TimeSpan.TryParse(timecode.Replace(',', '.'), out result))
				{
					int nbOfMs = (int)result.TotalMilliseconds;
					return nbOfMs;
				}
			}
			return -1;
		}

	}
}
