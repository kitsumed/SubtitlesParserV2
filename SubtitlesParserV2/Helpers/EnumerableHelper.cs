using System.Collections.Generic;

namespace SubtitlesParserV2.Helpers
{
	public static class EnumerableHelper
	{
		public static IEnumerable<T> Peekable<T>(this IEnumerable<T> source, out bool hasElements)
		{
			var enumerator = source.GetEnumerator();
			hasElements = enumerator.MoveNext();
			return Impl(enumerator, hasElements);

			static IEnumerable<T> Impl(IEnumerator<T> enumerator, bool hasElements)
			{
				using (enumerator)
				{
					if (hasElements)
					{
						yield return enumerator.Current;
						while (enumerator.MoveNext())
						{
							yield return enumerator.Current;
						}
					}
				}
			}
		}
	}
}