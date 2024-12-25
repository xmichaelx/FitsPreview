using System;
using System.Collections.Generic;
using System.Linq;

namespace FitsPreview
{
    // http://dark.physics.ucdavis.edu/~hal/lco40/lco40App/zscale.py
    public class ZScale 
    {
        private const double MAX_REJECT = 0.5;
        private const int MIN_NPIXELS = 5;
        private const int GOOD_PIXEL = 0;
        private const int BAD_PIXEL = 1;
        private const double KREJ = 2.5;
        private const int MAX_ITERATIONS = 5;

        public static (double,double) GetZScale(Array image, int nsamples = 1000, double contrast = 0.25)//, bpmask= None, zmask= None)
        {
            /*
                Implement IRAF zscale algorithm
                nsamples=1000 and contrast=0.25 are the IRAF display task defaults
                bpmask and zmask not implemented yet
                image is a 2-d numpy array
                returns (z1, z2)
                */
            //     # Sample the image
            if (image.Rank != 2)
                throw new ArgumentException("Image must be 2D");

            double[] samples = ZscSample(image, nsamples).ToArray();
            int npix = samples.Count();
            Array.Sort(samples);
            double zmin = samples.First();
            double zmax = samples.Last();
            //  # For a zero-indexed array
            int center_pixel = (npix - 1) / 2;
            double median;
            if (npix % 2 == 1)
                median = samples[center_pixel];
            else
                median = 0.5 * (samples[center_pixel] + samples[center_pixel + 1]);

            //                    # Fit a line to the sorted array of samples

            int minpix = (int)Math.Max(MIN_NPIXELS, npix * MAX_REJECT);
            int ngrow = (int)Math.Max(1, npix * 0.01);
            (int ngoodpix, double zstart, double zslope) = ZscFitLine(samples, npix, KREJ, ngrow, MAX_ITERATIONS);

            double z1 ;
            double z2;
            if (ngoodpix < minpix)
            {
                z1 = zmin;
                z2 = zmax;
            }
            else
            {
                if (contrast > 0)
                    zslope = zslope / contrast;
                z1 = Math.Max(zmin, median - (center_pixel - 1) * zslope);
                z2 = Math.Min(zmax, median + (npix - center_pixel) * zslope);
            }

            return (z1, z2);

        }

        private static IEnumerable<double> ZscSample(Array image, int maxpix)
        {
            //# Figure out which pixels to use for the zscale algorithm
            //# Returns the 1-d array samples
            //# Don't worry about the bad pixel mask or zmask for the moment
            //# Sample in a square grid, and return the first maxpix in the sample
            int nc = image.GetLength(0);
            int nl = image.GetLength(1);
            double[] samples = new double[maxpix];
            int count = 0;
            int stride = (int)Math.Max(1.0, Math.Sqrt((nc - 1) * (nl - 1) / (float)maxpix));
            for (int i = 0; i < nc; i += stride)
            {
                for (int j = 0; j < nl; j += stride)
                {
                    if (count >= maxpix)
                    {
                        return samples;
                    }
                    samples[count++] = Convert.ToDouble(image.GetValue(i, j));
                }
            }

            return samples;
        }

        private static (int, double, double) ZscFitLine(double[] samples, int npix, double krej, int ngrow, int maxiter)
        {
            //# First re-map indices from -1.0 to 1.0

            double xscale = 2.0 / (npix - 1);
            double[] xnorm = Enumerable.Range(0, npix).Select(x => x * xscale - 1.0).ToArray();

            int ngoodpix = npix;
            int minpix = (int)Math.Max(MIN_NPIXELS, npix * MAX_REJECT);
            int last_ngoodpix = npix + 1;

            //  # This is the mask used in k-sigma clipping.  0 is good, 1 is bad
            var badpix = new int[npix];

            //# Iterate
            double intercept = 0;
            double slope = 0;

            for (int niter = 0; niter < maxiter; ++niter)
            {
                if ((ngoodpix >= last_ngoodpix) || (ngoodpix < minpix))
                    break;

                //# Accumulate sums to calculate straight line fit
                int[] goodpixels = NonZero(badpix.Select(x => x == GOOD_PIXEL)).ToArray();
                double sumx = Filter(xnorm, goodpixels).Sum();
                double sumxx = SyncZip(Filter(xnorm, goodpixels), Filter(xnorm, goodpixels), (a, b) => a * b).Sum();
                double sumxy = SyncZip(Filter(xnorm, goodpixels), Filter(samples, goodpixels), (a, b) => a * b).Sum();
                double sumy = Filter(samples, goodpixels).Sum();
                int sum = goodpixels.Count();

                double delta = sum * sumxx - sumx * sumx;
                //# Slope and intercept
                intercept = (sumxx * sumy - sumx * sumxy) / delta;
                slope = (sum * sumxy - sumx * sumy) / delta;

                //# Subtract fitted line from the data array
                double[] fitted = xnorm.Select(it => it * slope + intercept).ToArray();
                double[] flat = SyncZip(samples, fitted, (a, b) => a - b).ToArray();

                //# Compute the k-sigma rejection threshold
                double? mean;
                double? sigma;
                (ngoodpix, mean, sigma) = ZscComputeSigma(flat, badpix, npix);

                double threshold = sigma.Value * krej;

                //# Detect and reject pixels further than k*sigma from the fitted line
                double lcut = -threshold;
                double hcut = threshold;
                int[] below = NonZero(flat.Select(x => x < lcut)).ToArray();
                int[] above = NonZero(flat.Select(x => x > hcut)).ToArray();

                Set(badpix, below, BAD_PIXEL);
                Set(badpix, above, BAD_PIXEL);

                //# Convolve with a kernel of length ngrow
                int[] conv = new int[npix];
                for (int i = 0; i < npix; i++)
                {
                    int ssum = 0;
                    for (int j = -ngrow / 2; j < ngrow / 2; j++)
                    {
                        int idx = i + j;
                        if (idx >= 0 && idx < npix)
                        {
                            ssum += badpix[idx];
                        }
                    }
                    conv[i] = ssum;
                }

                for (int i = 0; i < npix; i++)
                {
                    badpix[i] = conv[i];
                }


                ngoodpix = NonZero(badpix.Select(x => x == GOOD_PIXEL)).Count();
            }

            //# Transform the line coefficients back to the X range [0:npix-1]
            double zstart = intercept - slope;
            double zslope = slope * xscale;

            return (ngoodpix, zstart, zslope);
        }

        private static void Set<T>(T[] array, int[] indices, T value)
        {
            foreach (var i in indices)
                array[i] = value;
        }

        private static IEnumerable<double> SyncZip(IEnumerable<double> a, IEnumerable<double> b, Func<double, double, double> f)
        {
            if (a.Count() != b.Count())
                throw new ArgumentException();
            return a.Zip(b, f);
        }

        private static IEnumerable<T> Filter<T>(IReadOnlyList<T> array, int[] indices)
        {
            foreach (int i in indices)
                yield return array[i];
        }

        private static IEnumerable<int> NonZero(IEnumerable<bool> array)
        {
            int index = 0;
            foreach (bool v in array)
            {
                if (v)
                    yield return index;
                ++index;
            }
        }

        private static (int, double?, double?) ZscComputeSigma(double[] flat, int[] badpix, int npix)
        {
            //# Compute the rms deviation from the mean of a flattened array.
            //# Ignore rejected pixels

            //# Accumulate sum and sum of squares
            int[] goodpixels = NonZero(badpix.Select(x => x == GOOD_PIXEL)).ToArray();
            double sumz = Filter(flat, goodpixels).Sum();
            double sumsq = SyncZip(Filter(flat, goodpixels), Filter(flat, goodpixels), (a, b) => a * b).Sum();
            int ngoodpix = goodpixels.Count();

            double? sigma;
            double? mean;

            if (ngoodpix == 0)
            {
                mean = null;
                sigma = null;
            }
            else if (ngoodpix == 1)
            {
                mean = sumz;
                sigma = null;
            }
            else
            {
                mean = sumz / ngoodpix;
                double temp = sumsq / (ngoodpix - 1) - sumz * sumz / (ngoodpix * (ngoodpix - 1));
                if (temp < 0)
                    sigma = 0.0;
                else
                    sigma = Math.Sqrt(temp);
            }
            return (ngoodpix, mean, sigma);

        }
    }
}