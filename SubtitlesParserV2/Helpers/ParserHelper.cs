using System;
using System.Text;
using System.Xml;

namespace SubtitlesParserV2.Helpers
{
	/// <summary>
	/// This class contains helper methods for parsing subtitles.
	/// </summary>
	internal static class ParserHelper
    {
		/// <summary>
		/// Takes an string, prase it as a <see cref="TimeSpan"/> timecode and turn it into milliseconds.
		/// </summary>
		/// <remarks>
		/// <strong>Only uses this method when the line you are reading does not require format specific formatting.</strong>
		/// </remarks>
		/// <param name="s">The string to parse</param>
		/// <returns>The parsed timecode in milliseconds. If the parsing was unsuccessful, -1 is returned.</returns>
		internal static int ParseTimeSpanLineAsMilliseconds(string s)
		{
			TimeSpan result;
			if (TimeSpan.TryParse(s, out result))
			{
				int nbOfMs = (int)result.TotalMilliseconds;
				return nbOfMs;
			}
			else
			{
				return -1;
			}
		}

		/// <summary>
		/// Takes an xml reader and reads the inner elements (childs) to get all of the text values.
		/// All texte will be appended together without adding or removing spaces, the final returned result is trimmed.
		/// </summary>
		/// <remarks>
		/// <strong>Will only read child elements with localnames : span,font,string,b,u,i</strong>
		/// </remarks>
		/// <param name="reader">The xml reader</param>
		/// <returns>All child elements text appended together and trimmed</returns>
		internal static string XmlReadCurrentElementInnerText(XmlReader reader)
		{
			StringBuilder textBuilder = new StringBuilder();

			// Ensure our current element has at least one child
			if (!reader.IsEmptyElement)
			{
				// Store the informations of your current element to know when we reach the end of it
				string rootElementName = reader.LocalName;
				int rootElementDepth = reader.Depth;
				while (reader.Read())
				{
					if (reader.NodeType == XmlNodeType.Text)
					{
						textBuilder.Append(reader.Value);
					}
					else if (reader.NodeType == XmlNodeType.Element && (reader.LocalName == "span" || reader.LocalName == "font" || reader.LocalName == "b" || reader.LocalName == "u" || reader.LocalName == "i" || reader.LocalName == "string"))
					{
						// Read the content of (<span> / other name) and it's childs
						textBuilder.Append(XmlReadCurrentElementInnerText(reader));
					}
					// If we reach the end of the current element (no more childs), we stop reading
					else if (reader.NodeType == XmlNodeType.EndElement && reader.LocalName == rootElementName && rootElementDepth == reader.Depth) break;
				}
			}

			return textBuilder.ToString().Trim();
		}
	}
}
