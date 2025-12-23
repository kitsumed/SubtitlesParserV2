using SubtitlesParserV2.Helpers;
using SubtitlesParserV2.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Web;

namespace SubtitlesParserV2.Formats.Parsers
{
	/// <summary>
	/// Configuration model for the Sami parser.
	/// </summary>
	public class SamiParserConfig
	{
		/// <summary>
		/// By default, will define itself to the language used by the first subtitle found by the parser.
		/// If you define this value, only subtitle in the language matching what you defined
		/// will be parsed. Example: ENUSCC
		/// </summary>
		public string? TargetLanguage { get; set; }
	}
	/// <summary>
	/// Parser for the .sami/.smi subtitles files (html-like).
	/// <strong>NOTE</strong>: Only support time parsing in ms (Microsoft DirectShow and Windows Media Player support only milliseconds)
	/// </summary>
	/// <!--
	/// Sources:
	/// https://learn.microsoft.com/en-us/previous-versions/windows/desktop/dnacc/understanding-sami-1.0#sami-parameters
	/// https://en.wikipedia.org/wiki/SAMI
	/// https://docs.fileformat.com/video/sami/
	/// -->
	internal class SamiParser : ISubtitlesParser, ISubtitlesParser<SamiParserConfig>
	{
		public List<SubtitleModel> ParseStream(Stream stream, Encoding encoding)
		{
			return ParseStream(stream, encoding, new SamiParserConfig());
		}
		public List<SubtitleModel> ParseStream(Stream samiStream, Encoding encoding, SamiParserConfig config)
		{
			StreamHelper.ThrowIfStreamIsNotSeekableOrReadable(samiStream);
			// seek the beginning of the stream
			samiStream.Position = 0;

			// Create a StreamReader & configure it to leave the main stream open when disposing
			using StreamReader reader = new StreamReader(samiStream, encoding, true, 4096, true);

			List<SubtitleModel> items = new List<SubtitleModel>();
			List<string> linesPerBlock = new List<string>();

			// Store if we are reading in the body or not
			bool inBody = false;
			// Store the sync block begenning time
			int syncBlockStartTimeMs = -1;

			// Read the first line
			string? line = reader.ReadLine();
			// Ensure the file is a sami file by verifying the first line
			if (!line?.Equals("<SAMI>", StringComparison.OrdinalIgnoreCase) ?? true) throw new ArgumentException("Could not find SAMI element at line 1.");
			// Loop until last line (lastText & lastTimeMs) was processed (are null), +1 loop after end of stream
			do
			{
				// Read a new line
				line = reader.ReadLine();
				// If the line is null or empty, re-evaluate the while condition and read next line
				if (string.IsNullOrEmpty(line)) continue;

				// Verify if we are inside the body (where the subtitle are)
				if (inBody)
				{
					// Verify if we exited the body
					if (line.StartsWith("</BODY>", StringComparison.OrdinalIgnoreCase))
					{
						inBody = false;
						continue; // exit body execution as we are no longer inside the body
					}

					// Verify for a new sync block (Parse the time of the current line)
					if (line.StartsWith("<SYNC", StringComparison.OrdinalIgnoreCase))
					{
						int startIndex = line.IndexOf("Start=", StringComparison.OrdinalIgnoreCase) + 6;
						int endIndex = line.IndexOf('>', startIndex);
						// Store the new sync block (subtitle) start time
						syncBlockStartTimeMs = ParseSamiTime(line.Substring(startIndex, endIndex - startIndex).Trim()) ?? -1;
						// We now have the end time of the previous sync block, so we edit the SubtitleModel end time and
						// clear out lines of the previous sync block
						if (linesPerBlock.Count >= 1)
						{
							// This edit the value of the existing SubtitleModel (of the previous sync block)
							SubtitleModel previousSyncBlockSubtitles = items.Last();
							previousSyncBlockSubtitles.EndTime = syncBlockStartTimeMs;
							linesPerBlock = new List<string>(); // Clear lines previous sync block
						}
					}

					// Parse the text from the current line
					(string? text, string? textLanguage) = GetTextFromLine(line);
					// Ensure text was found
					if (!string.IsNullOrEmpty(text) && !string.IsNullOrEmpty(textLanguage))
					{
						// If target language is defined, do nothing, else, set the first language we found
						// as the target language. Thus, all lyrics from now on will need to have the same language.
						config.TargetLanguage = config.TargetLanguage != null ? config.TargetLanguage : textLanguage;
						// Ensure that the subtitle we are parsing is the same language as the targeted language
						if (config.TargetLanguage.Equals(textLanguage, StringComparison.OrdinalIgnoreCase))
						{
							linesPerBlock.Add(HttpUtility.HtmlDecode(text).Trim());
						}
					}

					// Handle SYNC block closure
					if (line.StartsWith("</SYNC>", StringComparison.OrdinalIgnoreCase) || line.EndsWith("</SYNC>", StringComparison.OrdinalIgnoreCase)) // Handle single line and multiline sync blocks
					{
						// If >= 1, we create a subtitleModel for the end of this sync block
						if (linesPerBlock.Count >= 1)
						{
							SubtitleModel appendedModel = new SubtitleModel()
							{
								EndTime = -1, // We can't know yet, will be updated at the begenning of the next SYNC BLOCK
								StartTime = syncBlockStartTimeMs, // Was defined in the SYNC block opening handling
								Lines = linesPerBlock
							};
							items.Add(appendedModel);
						}
						continue; // Go to next line in stream directly
					}
				}
				else if (line.StartsWith("<BODY>", StringComparison.OrdinalIgnoreCase)) // Verify if we entered the body
				{
					inBody = true;
				}
			} while (line != null); // Loop until end of stream

			// Ensure we at least found 1 valid item
			if (items.Count >= 1)
			{
				return items;
			}
			else
			{
				throw new ArgumentException("Stream is not in a valid Sami format");
			}
		}

		/// <summary>
		/// Parse sami text using the ms format only (assume sami uses MS time).
		/// </summary>
		/// <!--
		/// We could detect other time format implemented by third-party programs,
		/// but doc says time metrics should be MS by default:
		///	<SAMIParam>
		///	Metrics {time:ms;}
		///	Spec {MSFT:1.0;}
		///	</SAMIParam >
		/// ]]>
		/// -->
		/// <param name="text">The text to parse</param>
		/// <returns>The time in milliseconds or null if it failed</returns>
		private static int? ParseSamiTime(string text)
		{
			bool success = int.TryParse(text, out int time);
			if (success) return time;
			return null;
		}

		/// <summary>
		/// Parse a Sami stream to get the text of the current line along with it's language
		/// </summary>
		/// <param name="line">The line to parse</param>
		/// <returns>A ValueTuple with the text as Item2 and the targetLanguage as Item2</returns>
		private static (string? text, string? targetLanguage) GetTextFromLine(string line)
		{
			string? text = null;
			string? targetLanguage = null;
			// Directly try to find a P element as some .sami files have the sync block and the P Element on the same line
			int pIndex = line.IndexOf("<P", StringComparison.OrdinalIgnoreCase);
			// Ensure the position of the P element was found on the line
			if (pIndex >= 1)
			{
				// Get the class (language) of the P element (subtitle text) we found
				int pClassIndex = line.IndexOf("Class=", pIndex, StringComparison.OrdinalIgnoreCase) + 6;
				string pClassName = string.Empty;
				if (pClassIndex >= 1)
				{
					/* Here we have two possible way of finding the end of the attribute, a space, meaning a new attribute is
					 * starting, or a tag ">" meaning it's the end of the P element tag. Ex : <p Class=value-here>
					 * We ensure to take the index with the smallest number, to prevent issues where the space could have been
					 * found but at a index position after the ">" tag.
					 */
					int classEndSpace = line.IndexOf(" ", pClassIndex);
					int classEndTag = line.IndexOf(">", pClassIndex);
					int classEndIndex = classEndSpace >= 1 && (classEndSpace < classEndTag) ? classEndSpace : classEndTag;
					// Get the class (language name)
					targetLanguage = line.Substring(pClassIndex, classEndIndex - pClassIndex).Trim();
				}

				// Find the closure of the P element (html first part/creation)
				int textStart = line.IndexOf(">", pIndex) + 1;
				// Find the closure of the P element (html last part/closure)
				int textEnd = line.IndexOf("</P>", textStart);
				text = line.Substring(textStart, textEnd - textStart);
				return (text, targetLanguage);
			}
			return (null, null);
		}
	}
}
