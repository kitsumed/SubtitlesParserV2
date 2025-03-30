using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;
using SubtitlesParserV2.Helpers;
using SubtitlesParserV2.Logger;
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
		private static readonly Type CurrentType = typeof(YttXmlParser);
		// Alternative for static class, create a logger with the full namespace name
		private static readonly ILogger _logger = LoggerManager.GetLogger(CurrentType.FullName ?? CurrentType.Name);

		public List<SubtitleModel> ParseStream(Stream xmlStream, Encoding encoding)
		{
			StreamHelper.ThrowIfStreamIsNotSeekableOrReadable(xmlStream);
			// seek the beginning of the stream
			xmlStream.Position = 0;
			List<SubtitleModel> items = new List<SubtitleModel>();

			// parse xml stream
			XElement xElement = XElement.Load(xmlStream);

			// Try to get lyrics in SRV3 format (P element)
			IEnumerable<XElement> nodeList = xElement.Descendants("p").Peekable(out var nodeListAny);
			if (!nodeListAny)
			{
				// Fallback to SRV2 & SRV1 format (Text element)
				nodeList = xElement.Descendants("text");
			}

			foreach (XElement node in nodeList)
			{
				try
				{
					float start;
					float duration;
					// Try to get the start & end time for SRV3 & SRV2 format (already in MS)
					string startString = node.Attribute("t")?.Value ?? string.Empty;
					string durString = node.Attribute("d")?.Value ?? string.Empty;
					_ = float.TryParse(startString, default, CultureInfo.InvariantCulture, out start);
					_ = float.TryParse(durString, default, CultureInfo.InvariantCulture, out duration);
					// Fallback to SRV1 format (In seconds)
					if (string.IsNullOrEmpty(startString) && string.IsNullOrEmpty(durString)) 
					{
						startString = node.Attribute("start")?.Value ?? string.Empty;
						durString = node.Attribute("dur")?.Value ?? string.Empty;
						_ = float.TryParse(startString, NumberStyles.Float, CultureInfo.InvariantCulture, out start);
						_ = float.TryParse(durString, NumberStyles.Float, CultureInfo.InvariantCulture, out duration);
						start = start * 1000; // Convert S to MS
						duration = duration * 1000; // Convert duration S to MS.
					}

					// Get the text and html decode it as some versions (SRV1 & SRV2) uses html encoding
					// for certains characters ( ' > &#39;t). Before and after text spaces are also removed.
					string text = WebUtility.HtmlDecode(node.Value).Trim();

					items.Add(new SubtitleModel()
					{
						StartTime = (int)start,
						EndTime = (int)(start + duration), // Calculate the "end" time with the duration of the subtitle.
						Lines = new List<string>() { text }
					});
				}
				catch (Exception ex)
				{
					_logger.LogDebug(ex, "Exception raised when parsing xml node {node}: {ex}", node, ex.Message);
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
