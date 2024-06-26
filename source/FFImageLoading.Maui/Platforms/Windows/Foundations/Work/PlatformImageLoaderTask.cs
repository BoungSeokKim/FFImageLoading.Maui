﻿using FFImageLoading.Cache;
using FFImageLoading.Config;
using FFImageLoading.Extensions;
using FFImageLoading.Helpers;
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Xaml.Media.Imaging;
using FFImageLoading.Decoders;
using System.Collections.Generic;

namespace FFImageLoading.Work
{
    public class PlatformImageLoaderTask<TImageView> : ImageLoaderTask<BitmapHolder, BitmapSource, TImageView> where TImageView : class
    {
		public PlatformImageLoaderTask(
			IImageService imageService,
			ITarget<BitmapSource, TImageView> target,
			TaskParameter parameters)
			: base(imageService, target, parameters)
		{
			MemoryCache = ImageCache.Instance;
		}

        public async override Task Init()
        {
            await base.Init().ConfigureAwait(false);
        }

        protected override async Task SetTargetAsync(BitmapSource image, bool animated)
        {
			if (Target == null)
				return;
            
            await ImageService.Dispatcher.PostAsync(() =>
            {
                ThrowIfCancellationRequested();
                PlatformTarget.Set(this, image, animated);
            }).ConfigureAwait(false);
        }

        protected override int DpiToPixels(int size, double scale)
        {
            return ImageService.DpToPixels(size, scale);
        }

        protected override IDecoder<BitmapHolder> ResolveDecoder(ImageInformation.ImageType type)
        {
            switch (type)
            {
                case ImageInformation.ImageType.GIF:
					return new GifDecoder(ImageService);
				case ImageInformation.ImageType.WEBP:
                default:
                    return new BaseDecoder(ImageService);
            }
        }

        protected override async Task<BitmapHolder> TransformAsync(BitmapHolder bitmap, IList<ITransformation> transformations, string path, ImageSourceType sourceType, bool isPlaceholder)
        {
            //await StaticLocks.DecodingLock.WaitAsync(CancellationTokenSource.Token).ConfigureAwait(false); // Applying transformations is both CPU and memory intensive
            ThrowIfCancellationRequested();

            try
            {
                foreach (var transformation in transformations)
                {
                    ThrowIfCancellationRequested();

                    var old = bitmap;

                    try
                    {
                        IBitmap bitmapHolder = transformation.Transform(bitmap, path, sourceType, isPlaceholder, Key);
                        bitmap = bitmapHolder.ToNative();
                    }
                    catch (Exception ex)
                    {
                        ImageService.Logger.Error(string.Format("Transformation failed: {0}", transformation.Key), ex);
                        throw;
                    }
                    finally
                    {
                        if (old != null && old != bitmap && old.PixelData != bitmap.PixelData)
                        {
                            old.FreePixels();
                            old = null;
                        }
                    }
                }
            }
            finally
            {
				//StaticLocks.DecodingLock.Release();
            }

            return await Task.FromResult(bitmap);
        }

        protected override async Task<BitmapSource> GenerateImageFromDecoderContainerAsync(IDecodedImage<BitmapHolder> decoded, ImageInformation imageInformation, bool isPlaceholder)
        {
            if (decoded.IsAnimated)
            {
                return await decoded.ToAnimatedImageAsync(ImageService.Dispatcher).ConfigureAwait(false);
			}
            else
            {
                try
                {
					if (decoded.Image.HasBitmapImage)
						return decoded.Image.BitmapImage;

                    if (decoded.Image.HasWriteableBitmap)
                        return decoded.Image.WriteableBitmap;

                    return await decoded.Image.ToBitmapImageAsync(ImageService.Dispatcher).ConfigureAwait(false);
                }
                finally
                {
                    decoded.Image.FreePixels();
                    decoded.Image = null;
                }
            }
        }
    }
}
