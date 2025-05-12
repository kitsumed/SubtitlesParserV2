using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using System.Xml;
using SubtitlesParserV2.Helpers;
using SubtitlesParserV2.Models;

namespace SubtitlesParserV2.Formats.Parsers
{
	/// <summary>
	/// Parser for .ytt files (Youtube Timed Text).
	/// </summary>
	/// <!--
	/// Sources (Unofficial) : https://github.com/FyraLabs/yttml/blob/main/crates/srv3-ttml/internals/srv3-format.md
	/// -->
	internal class YttXmlParser : ISubtitlesParser
	{
		public List<SubtitleModel> ParseStream(Stream xmlStream, Encoding encoding)
		{
			StreamHelper.ThrowIfStreamIsNotSeekableOrReadable(xmlStream);
			// seek the beginning of the stream
			xmlStream.Position = 0;

			List<SubtitleModel> items = new List<SubtitleModel>();
			// Read the xml stream line by line
			using XmlReader reader = XmlReader.Create(xmlStream, new XmlReaderSettings() { IgnoreWhitespace = true, IgnoreComments = true });

			while (reader.Read()) 
			{
				// Search for subtitle elements (p for SRV3 and text for SRV1/SRV2)
				if (reader.NodeType == XmlNodeType.Element && (reader.Name == "p" || reader.Name == "text"))
				{
					float start;
					float duration = 0; // Default duration if parsing fails.

					// Try to get the start & end time for SRV3 & SRV2 format (already in MS)
					string startString = reader.GetAttribute("t") ?? string.Empty;
					string durString = reader.GetAttribute("d") ?? string.Empty;
					// Fallback to SRV1 format if parsing fail (In seconds)
					if (!float.TryParse(startString, NumberStyles.Float, CultureInfo.InvariantCulture, out start) && !float.TryParse(durString, NumberStyles.Float, CultureInfo.InvariantCulture, out duration))
					{
						startString = reader.GetAttribute("start") ?? string.Empty;
						durString = reader.GetAttribute("dur") ?? string.Empty;
						if (float.TryParse(startString, NumberStyles.Float, CultureInfo.InvariantCulture, out start)) 
						{
							start = start * 1000; // Convert S to MS
						} else start = -1; // Could not find start time, default "invalid" value is -1

						if (float.TryParse(durString, NumberStyles.Float, CultureInfo.InvariantCulture, out duration)) 
						{
							duration = duration * 1000; // Convert duration S to MS.
						}
					}

					// We need to read the next node to get the text value.
					if (reader.Read() && reader.NodeType == XmlNodeType.Text) 
					{
						// Get the text and html decode it as some versions (SRV1 & SRV2) uses html encoding
						// for certains characters ( ' > &#39;t). Before and after text spaces are also removed.
						string text = WebUtility.HtmlDecode(reader.Value.Trim());

						items.Add(new SubtitleModel()
						{
							StartTime = (int)start,
							EndTime = (int)(start + duration), // Calculate the "end" time with the duration of the subtitle.
							Lines = new List<string>() { text }
						});
					}
				}
			}

			if (items.Count >= 1)
			{
				return items;
			}
			else
			{
				throw new ArgumentException("Stream is not in a valid Youtube XML format");
			}
		}
	}
}
