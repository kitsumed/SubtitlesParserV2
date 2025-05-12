using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Xml;
using SubtitlesParserV2.Helpers;
using SubtitlesParserV2.Models;


namespace SubtitlesParserV2.Formats.Parsers
{
	/// <summary>
	/// Parser for .ttml / .dfxp files.
	/// </summary>
	/// File Formats: Support TTML 1.0 and 2.0, dfxp (Distribution Format Exchange Profile) uses TTML 1.0
	/// <!--
	/// Sources:
	/// https://www.w3.org/TR/2018/REC-ttml1-20181108/#vocabulary-namespaces
	/// -->
	internal class TtmlParser : ISubtitlesParser
    {
		public List<SubtitleModel> ParseStream(Stream xmlStream, Encoding encoding)
        {
			StreamHelper.ThrowIfStreamIsNotSeekableOrReadable(xmlStream);
			// seek the beginning of the stream
			xmlStream.Position = 0;
			List<SubtitleModel> items = new List<SubtitleModel>();

			// Read the xml stream line by line
			using XmlReader reader = XmlReader.Create(xmlStream, new XmlReaderSettings { IgnoreWhitespace = true, IgnoreComments = true });

			// Value for parsing timecode for ttml file using ticks only, null by default (disabled)
			long? definedTickRate = null;

			while (reader.Read()) 
			{
				if (reader.NodeType == XmlNodeType.Element) 
				{
					// Parse the <tt> element attributes to find the tickRate
					if (reader.Name == "tt" && reader.HasAttributes)
					{
						// Loop through the attributes of the <tt> element
						while (reader.MoveToNextAttribute())
						{
							if (reader.LocalName == "tickRate" && long.TryParse(reader.Value, out long parsedTickRate))
							{
								definedTickRate = parsedTickRate;
							}
						}
						reader.MoveToElement(); // Set our reader back to the <tt> element
					}

					// Parse the <p> element (subtitle)
					if (reader.LocalName == "p")
					{
						// Parse time
						string beginString = reader.GetAttribute("begin") ?? string.Empty;
						int startMs = ParseTimecode(beginString, definedTickRate);

						string endString = reader.GetAttribute("end") ?? string.Empty;
						int endMs = ParseTimecode(endString, definedTickRate);

						// Parse subtitle text
						string text = ParserHelper.XmlReadCurrentElementInnerText(reader);
						if (text != null)
						{
							items.Add(new SubtitleModel()
							{
								StartTime = startMs,
								EndTime = endMs,
								Lines = new List<string> { text }
							});
						}
					}
				}
			}

            if (items.Count >= 1)
            {
                return items;
            }
            throw new ArgumentException("Stream is not in a valid TTML format, or represents empty subtitles");
        }

		/// <summary>
		/// Takes an TTML timecode as a string and parses it into a int (millisegonds).
		/// </summary>
		/// <remarks>
		/// A TTML timecode can reads as follows: 
		/// <code>
		/// 5.0s (default version, based on time in seconds)
		/// 79249170t (ticks version, dependant on tickRate)
		/// </code>
		/// </remarks>
		/// <param name="s">The timecode to parse</param>
		/// <param name="tickRate">If found in the file ttp namespace, the tickRate used for time using Ticks format.</param>
		/// <returns>The parsed string timecode in milliseconds. If the parsing was unsuccessful, -1 is returned</returns>
		private static int ParseTimecode(string s, long? tickRate = 10000000)
        {
            // Ensure null values get a "default" value
            tickRate = tickRate.HasValue ? tickRate : 10000000;

			// Get time in 5.0s format (TimeSpan format)
			if (TimeSpan.TryParse(s, out TimeSpan result))
            {
                return (int)result.TotalMilliseconds;
            }
			// Get time in 79249170t format (ticks), this format is used by netflix for example.
			// According to https://www.w3.org/TR/ttml1/#parameter-attribute-tickRate, the tickRate is egual to 1 segonds.
			if (s.EndsWith('t') && long.TryParse(s.TrimEnd('t'), out long ticks))
            {
                // Divide ticks by it's tickRate (defined by the person who made that specific ttml file),
                // result is the time in segonds, we convert it into MS.
				return (int)TimeSpan.FromSeconds(ticks / tickRate.Value).TotalMilliseconds;
            }
			// TODO : Add support for frame based time https://www.w3.org/TR/ttml1/#parameter-attribute-frameRate
			return -1;
        }
    }
}