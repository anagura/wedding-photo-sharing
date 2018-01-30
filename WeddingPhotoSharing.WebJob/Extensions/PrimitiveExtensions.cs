using System;

namespace WeddingPhotoSharing.WebJob
{
    public static class PrimitiveExtensions
    {
        public static int Ceiling(this double source)
        {
            return (int)Math.Ceiling(source);
        }

        public static int Ceiling(this float source)
        {
            return (int)Math.Ceiling(source);
        }

        public static int Floor(this double source)
        {
            return (int)Math.Floor(source);
        }

        public static int Floor(this float source)
        {
            return (int)Math.Floor(source);
        }
    }
}
