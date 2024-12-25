namespace FitsPreview
{
    internal class Utils
    {
        public static void Scale(Array input, byte[] pixels)
        {
            var (zmin, zmax) = ZScale.GetZScale(input);
            var diff = zmax - zmin;

            var width = input.GetLength(0);
            var height = input.GetLength(1);

            switch (input)
            {
                case float[,] floatArray:
                    ScaleSpecific(floatArray, width, height, pixels, zmin, diff);
                    break;

                case double[,] doubleArray:
                    ScaleSpecific(doubleArray, width, height, pixels, zmin, zmax);
                    break;

                case short[,] shortArray:
                    ScaleSpecific(shortArray, width, height, pixels, zmin, zmax);
                    break;

                default:
                    throw new ArgumentException("Unsupported input type.");
            }
        }

        private static void ScaleSpecific(short[,] input, int width, int height, byte[] pixels, double zmin, double diff)
        {
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    var value = 255 * (input[x, y] - zmin) / diff;
                    pixels[y * width + x] = (byte)(value > 255 ? 255 : value < 0 ? 0 : value);
                }
            }
        }

        private static void ScaleSpecific(float[,] input, int width, int height, byte[] pixels, double zmin, double diff)
        {
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    var value = 255 * (input[x, y] - zmin) / diff;
                    pixels[y * width + x] = (byte)( value > 255 ? 255 : value < 0 ? 0 : value); 
                }
            }
        }

        private static void ScaleSpecific(double[,] input, int width, int height, byte[] pixels, double zmin, double diff)
        {
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    var value = 255 * (input[x, y] - zmin) / diff;
                    pixels[y * width + x] = (byte)(value > 255 ? 255 : value < 0 ? 0 : value);
                }
            }
        }
    }
}
