using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using SubtitlesParserV2.Helpers;
using SubtitlesParserV2.Logger;
using SubtitlesParserV2.Models;

namespace SubtitlesParserV2.Formats.Parsers
{
	/// <summary>
	/// Configuration model for the MicroDVD parser.
	/// </summary>
	public class MicroDvdParserConfig
	{
		/// <summary>
		/// If defined, will skip the automatic framerate detection and use
		/// this value when parsing the file.
		/// </summary>
		public float? Framerate { get; set; }

		/// <summary>
		/// Define the maximum number of lines the program will continue reading before exiting if it
		/// haven't found any lines in MicroDvd format.
		/// </summary>
		public int FirstLineSearchTimeout { get; set; } = 20;
	}
	/// <summary>
	/// Parser for MicroDVD .sub subtitles files.
	/// Will try to detect the framerate by default.
	/// </summary>
	/// <remarks>
	/// If no framerate are found, will default to 25. To force specific settings, you can use
	/// <see cref="SubtitleFormat.GetFormat(SubtitleFormatType)"/> and define the <see cref="SubtitleFormat.ParserInstance"/>
	/// as a <see cref="ISubtitlesParser{MicroDvdParserConfig}"/>.
	/// </remarks>
	/// <!--
	/// Sources:
	/// https://en.wikipedia.org/wiki/MicroDVD
	/// Example:
	/// {1}{1}29.970
	/// {0}{180}PIRATES OF THE WORLD|English by chicken
	/// {509}{629}Drink up water yo ho!
	/// {635}{755}We eat and don't give a hoot.
	/// -->
	internal class MicroDvdParser : ISubtitlesParser, ISubtitlesParser<MicroDvdParserConfig>
	{
		private static readonly Type CurrentType = typeof(MicroDvdParser);
		// Alternative for static class, create a logger with the full namespace name
		private static readonly ILogger _logger = LoggerManager.GetLogger(CurrentType.FullName ?? CurrentType.Name);

		// Properties -----------------------------------------------------------------------

		private const float defaultFrameRate = 25;
		private static readonly char[] _lineSeparators = { '|' };
        private static readonly Regex LineRegex = new Regex(@"^[{\[](-?\d+)[}\]][{\[](-?\d+)[}\]](.*)", RegexOptions.Compiled);

        // Methods -------------------------------------------------------------------------

        public List<SubtitleModel> ParseStream(Stream subStream, Encoding encoding)
		{
			return ParseStream(subStream, encoding, new MicroDvdParserConfig());
		}

		public List<SubtitleModel> ParseStream(Stream subStream, Encoding encoding, MicroDvdParserConfig config)
		{
			StreamHelper.ThrowIfStreamIsNotSeekableOrReadable(subStream);
			// seek the beginning of the stream
			subStream.Position = 0;

			// Create a StreamReader & configure it to leave the main stream open when disposing
			using StreamReader reader = new StreamReader(subStream, encoding, true, 4096, true);

			List<SubtitleModel> items = new List<SubtitleModel>();
			string? line = reader.ReadLine();
			// find the first relevant line
			while (line != null && !IsMicroDvdLine(line) && config.FirstLineSearchTimeout > 0)
			{
				line = reader.ReadLine();
				config.FirstLineSearchTimeout--;
			}

			if (line != null)
			{
				float frameRate;
				if (config.Framerate.HasValue) // If a framerate was given when calling the method, use it
				{
					frameRate = config.Framerate.Value;
				}
				else
				{
					// try to extract the framerate from the first line
					SubtitleModel firstItem = ParseLine(line, defaultFrameRate);
					if (firstItem.Lines != null && firstItem.Lines.Count >= 1)
					{
						bool success = TryExtractFrameRate(firstItem.Lines[0], out frameRate);
						if (!success)
						{
							_logger.LogWarning("Couldn't extract frame rate of sub file with first line {line}. We use the default frame rate: {frameRate}", line, defaultFrameRate);
							frameRate = defaultFrameRate;
							// We didn't find the framerate on the line, the line might be a subtitle so we add it to the list
							items.Add(firstItem);
						}
					}
					else
					{
						frameRate = defaultFrameRate;
					}
				}

				// Parse other lines
				line = reader.ReadLine();
				while (line != null)
				{
					if (!string.IsNullOrEmpty(line))
					{
						SubtitleModel item = ParseLine(line, frameRate);
						items.Add(item);
					}
					line = reader.ReadLine();
				}
			}

			if (items.Count >= 1)
			{
				return items;
			}
			else
			{
				throw new ArgumentException("Stream is not in a valid MicroDVD format");
			}
		}

		private static bool IsMicroDvdLine(string line)
		{
			return LineRegex.IsMatch(line);
		}

		/// <summary>
		/// Parses one line of the .sub file
		/// </summary>
		/// <!--
		/// Example:
		/// {0}{180}PIRATES OF THE WORLD|English subtitlez by chicken
		/// -->
		/// <param name="line">The .sub file line</param>
		/// <param name="frameRate">The frame rate with which the .sub file was created</param>
		/// <returns>The corresponding SubtitleItem</returns>
		private static SubtitleModel ParseLine(string line, float frameRate)
		{
			Match match = LineRegex.Match(line);
			if (match.Success && match.Groups.Count > 2)
			{
				string startFrame = match.Groups[1].Value;
				int start = (int)(1000 * double.Parse(startFrame) / frameRate);
				string endTime = match.Groups[2].Value;
				int end = (int)(1000 * double.Parse(endTime) / frameRate);
				string text = match.Groups[match.Groups.Count - 1].Value;
				string[] lines = text.Split(_lineSeparators);
				List<string> nonEmptyLines = lines.Where(l => !string.IsNullOrEmpty(l)).ToList();
				SubtitleModel item = new SubtitleModel
				{
					Lines = nonEmptyLines,
					StartTime = start,
					EndTime = end
				};

				return item;
			}
			else
			{
				throw new InvalidDataException($"The subtitle file line {line} is not in the micro dvd format. We stop the process.");
			}
		}

		/// <summary>
		/// Tries to extract the frame rate from a subtitle file line.
		/// </summary>
		/// <!--
		/// Supported formats are:
		/// {x}{y}25
		/// {x}{y}{...}23.976
		/// -->
		/// <param name="text">The subtitle file line</param>
		/// <param name="frameRate">The frame rate if we can parse it</param>
		/// <returns>True if the parsing was successful, false otherwise</returns>
		private static bool TryExtractFrameRate(string text, out float frameRate)
		{
			if (!string.IsNullOrEmpty(text))
			{
				bool success = float.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out frameRate);
				return success;
			}
			else
			{
				frameRate = defaultFrameRate;
				return false;
			}
		}
	}
}