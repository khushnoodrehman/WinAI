using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;
using WinAI.Data.Models;

namespace WinAI.Services
{
    /// <summary>
    /// Production-grade media storage manager.
    /// Handles persistent image saving, thumbnail generation, collision resistance,
    /// safe deletion, and orphan cleanup under LocalFolder/Media/.
    /// </summary>
    public sealed class ImageStorageService
    {
        private const string MediaFolderName = "Media";
        private const string ImagesFolderName = "Images";
        private const string ThumbnailsFolderName = "Thumbnails";
        private const int MaxThumbnailDimension = 320;

        private static readonly Lazy<ImageStorageService> _instance =
            new Lazy<ImageStorageService>(() => new ImageStorageService());

        public static ImageStorageService Instance => _instance.Value;

        private StorageFolder _imagesFolder;
        private StorageFolder _thumbnailsFolder;
        private bool _isInitialized;

        private ImageStorageService() { }

        public async Task InitializeAsync()
        {
            if (_isInitialized) return;

            try
            {
                var localFolder = ApplicationData.Current.LocalFolder;
                var mediaFolder = await localFolder.CreateFolderAsync(
                    MediaFolderName, CreationCollisionOption.OpenIfExists);

                _imagesFolder = await mediaFolder.CreateFolderAsync(
                    ImagesFolderName, CreationCollisionOption.OpenIfExists);

                _thumbnailsFolder = await mediaFolder.CreateFolderAsync(
                    ThumbnailsFolderName, CreationCollisionOption.OpenIfExists);

                _isInitialized = true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ImageStorageService] Initialization error: {ex.Message}");
            }
        }

        private async Task EnsureFoldersAsync()
        {
            if (!_isInitialized || _imagesFolder == null || _thumbnailsFolder == null)
            {
                await InitializeAsync();
            }
        }

        /// <summary>
        /// Saves an imported image file (from camera or gallery) into durable app-managed storage,
        /// generates an optimized downscaled thumbnail, and returns the AttachmentEntity metadata.
        /// </summary>
        public async Task<AttachmentEntity> SaveAttachmentAsync(
            StorageFile sourceFile,
            string conversationId,
            string messageId)
        {
            if (sourceFile == null) throw new ArgumentNullException(nameof(sourceFile));
            await EnsureFoldersAsync();

            string attachmentId = Guid.NewGuid().ToString("D");
            string ext = Path.GetExtension(sourceFile.Name)?.ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(ext) || ext == ".") ext = ".jpg";

            string mimeType = GetMimeTypeFromExtension(ext);
            string collisionResistantName = $"{attachmentId}{ext}";
            string thumbnailName = $"{attachmentId}_thumb.jpg";

            // 1. Copy original file to app-managed Images directory
            StorageFile destinationFile = await sourceFile.CopyAsync(
                _imagesFolder, collisionResistantName, NameCollisionOption.GenerateUniqueName);

            // Read actual file properties
            var basicProps = await destinationFile.GetBasicPropertiesAsync();
            long fileSizeBytes = (long)basicProps.Size;

            int width = 0;
            int height = 0;

            // 2. Read dimensions & generate downscaled thumbnail
            try
            {
                using (var stream = await destinationFile.OpenReadAsync())
                {
                    var decoder = await BitmapDecoder.CreateAsync(stream);
                    width = (int)decoder.PixelWidth;
                    height = (int)decoder.PixelHeight;

                    // Generate optimized thumbnail
                    await GenerateThumbnailAsync(decoder, thumbnailName);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ImageStorageService] Thumbnail generation error: {ex.Message}");
                // If thumbnail failed, copy destination as fallback thumbnail
                try
                {
                    await destinationFile.CopyAsync(_thumbnailsFolder, thumbnailName, NameCollisionOption.ReplaceExisting);
                }
                catch { }
            }

            var entity = new AttachmentEntity
            {
                Id = attachmentId,
                MessageId = messageId,
                ConversationId = conversationId,
                LocalFileName = destinationFile.Name,
                ThumbnailFileName = thumbnailName,
                OriginalFileName = sourceFile.Name,
                ContentType = mimeType,
                FileSizeBytes = fileSizeBytes,
                ImageWidth = width,
                ImageHeight = height,
                CreatedAtUtc = DateTime.UtcNow
            };

            return entity;
        }

        /// <summary>
        /// Generates a fast, low-memory thumbnail saved into the Thumbnails folder.
        /// </summary>
        private async Task GenerateThumbnailAsync(BitmapDecoder decoder, string thumbnailFileName)
        {
            uint origW = decoder.PixelWidth;
            uint origH = decoder.PixelHeight;

            uint thumbW = origW;
            uint thumbH = origH;

            if (origW > MaxThumbnailDimension || origH > MaxThumbnailDimension)
            {
                if (origW > origH)
                {
                    thumbW = MaxThumbnailDimension;
                    thumbH = (uint)((double)origH * MaxThumbnailDimension / origW);
                }
                else
                {
                    thumbH = MaxThumbnailDimension;
                    thumbW = (uint)((double)origW * MaxThumbnailDimension / origH);
                }
            }

            if (thumbW < 1) thumbW = 1;
            if (thumbH < 1) thumbH = 1;

            var pixelData = await decoder.GetPixelDataAsync(
                BitmapPixelFormat.Bgra8,
                BitmapAlphaMode.Premultiplied,
                new BitmapTransform { ScaledWidth = thumbW, ScaledHeight = thumbH },
                ExifOrientationMode.RespectExifOrientation,
                ColorManagementMode.ColorManageToSRgb);

            byte[] pixels = pixelData.DetachPixelData();

            var thumbFile = await _thumbnailsFolder.CreateFileAsync(
                thumbnailFileName, CreationCollisionOption.ReplaceExisting);

            using (var thumbStream = await thumbFile.OpenAsync(FileAccessMode.ReadWrite))
            {
                var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.JpegEncoderId, thumbStream);
                encoder.SetPixelData(
                    BitmapPixelFormat.Bgra8,
                    BitmapAlphaMode.Premultiplied,
                    thumbW,
                    thumbH,
                    96.0,
                    96.0,
                    pixels);
                await encoder.FlushAsync();
            }
        }

        /// <summary>
        /// Retrieves the StorageFile for a full-size image by local file name.
        /// </summary>
        public async Task<StorageFile> GetImageFileAsync(string localFileName)
        {
            if (string.IsNullOrWhiteSpace(localFileName)) return null;
            await EnsureFoldersAsync();

            try
            {
                var item = await _imagesFolder.TryGetItemAsync(localFileName);
                return item as StorageFile;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Retrieves the StorageFile for a thumbnail image by thumbnail file name.
        /// </summary>
        public async Task<StorageFile> GetThumbnailFileAsync(string thumbnailFileName)
        {
            if (string.IsNullOrWhiteSpace(thumbnailFileName)) return null;
            await EnsureFoldersAsync();

            try
            {
                var item = await _thumbnailsFolder.TryGetItemAsync(thumbnailFileName);
                if (item is StorageFile sf) return sf;

                // Fallback to original image if thumbnail is missing
                var orig = await _imagesFolder.TryGetItemAsync(thumbnailFileName.Replace("_thumb.jpg", ".jpg"));
                return orig as StorageFile;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Deletes the local image file and thumbnail associated with an attachment.
        /// </summary>
        public async Task DeleteAttachmentFilesAsync(string localFileName, string thumbnailFileName)
        {
            await EnsureFoldersAsync();

            if (!string.IsNullOrWhiteSpace(localFileName))
            {
                try
                {
                    var file = await _imagesFolder.TryGetItemAsync(localFileName);
                    if (file is StorageFile sf) await sf.DeleteAsync();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[ImageStorageService] Delete image failed: {ex.Message}");
                }
            }

            if (!string.IsNullOrWhiteSpace(thumbnailFileName))
            {
                try
                {
                    var thumb = await _thumbnailsFolder.TryGetItemAsync(thumbnailFileName);
                    if (thumb is StorageFile sf) await sf.DeleteAsync();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[ImageStorageService] Delete thumb failed: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Cleans up any files in Media/Images and Media/Thumbnails that are not referenced in SQLite.
        /// </summary>
        public async Task CleanupOrphanedFilesAsync(ISet<string> referencedFileNames)
        {
            if (referencedFileNames == null) return;
            await EnsureFoldersAsync();

            try
            {
                var imageFiles = await _imagesFolder.GetFilesAsync();
                foreach (var file in imageFiles)
                {
                    if (!referencedFileNames.Contains(file.Name))
                    {
                        try { await file.DeleteAsync(); } catch { }
                    }
                }

                var thumbFiles = await _thumbnailsFolder.GetFilesAsync();
                foreach (var thumb in thumbFiles)
                {
                    string baseName = thumb.Name.Replace("_thumb.jpg", "");
                    bool isReferenced = false;
                    foreach (var refName in referencedFileNames)
                    {
                        if (refName.StartsWith(baseName, StringComparison.OrdinalIgnoreCase))
                        {
                            isReferenced = true;
                            break;
                        }
                    }
                    if (!isReferenced)
                    {
                        try { await thumb.DeleteAsync(); } catch { }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ImageStorageService] Orphan cleanup error: {ex.Message}");
            }
        }

        public static string GetMimeTypeFromExtension(string extension)
        {
            switch (extension?.ToLowerInvariant())
            {
                case ".png": return "image/png";
                case ".webp": return "image/webp";
                case ".bmp": return "image/bmp";
                case ".gif": return "image/gif";
                default: return "image/jpeg";
            }
        }
    }
}
