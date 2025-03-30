using System.Collections.Generic;

namespace SubtitlesParserV2.Helpers
{
	internal static class EnumerableHelper
	{
		/// <summary>
		/// Method to verify if your IEnumerable has at least 1 element while not iterating over it.
		/// </summary>
		/// <typeparam name="T">The type of the IEnumerable collection</typeparam>
		/// <param name="source"></param>
		/// <param name="hasElements">Do the IEnumerable have at least 1 element?</param>
		/// <returns>IEnumerable</returns>
		public static IEnumerable<T> Peekable<T>(this IEnumerable<T> source, out bool hasElements)
		{
			IEnumerator<T> enumerator = source.GetEnumerator();
			// Try to iterate over the first element
			hasElements = enumerator.MoveNext();
			// Return our Enumerator implementation 
			return Impl(enumerator, hasElements);

			// Handle returning the first iterated element and iterating the next elements of the collection
			// Need to be a local method as our parent method have a OUT argument, which is not compatible with yield returns
			static IEnumerable<T> Impl(IEnumerator<T> enumerator, bool hasElements)
			{
				using (enumerator)
				{
					if (hasElements)
					{
						// First iterated element
						yield return enumerator.Current;
						// Iterate next elements until end of collection
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