using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WeddingPhotoSharing.WebJob
{
    public static class EnumerableExtensions
    {
        public static IEnumerable<List<T>> Buffer<T>(this IEnumerable<T> source, int count)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            return BufferImplements(source, count);
        }

        private static IEnumerable<List<T>> BufferImplements<T>(IEnumerable<T> source, int count)
        {
            var result = new List<T>(count);
            foreach (var item in source)
            {
                result.Add(item);
                if (result.Count == count)
                {
                    yield return result;
                    result = new List<T>(count);
                }
            }

            if (result.Count != 0)
                yield return result;
        }

        public static IEnumerable<(T item, int index)> Indexed<T>(this IEnumerable<T> source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));

            IEnumerable<(T item, int index)> impl()
            {
                var i = 0;
                foreach (var item in source)
                {
                    yield return (item, i);
                    ++i;
                }
            }

            return impl();
        }

    }
}
