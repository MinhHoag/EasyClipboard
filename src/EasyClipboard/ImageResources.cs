namespace EasyClipboard;

/// <summary>Resource checks instead of fixed photo dimensions or file-size cutoffs.</summary>
public static class ImageResources
{
    private static long AvailableMemory => Math.Max(0,
        GC.GetGCMemoryInfo().TotalAvailableMemoryBytes - GC.GetTotalMemory(false));

    // JSON buffering, UTF-16 base64, decoded bytes, history encryption and clipboard
    // copies coexist. Admit one upload at a time and leave room for those copies.
    public static long UploadBodyBudget => AvailableMemory / 8;

    public static void EnsureImageFits(uint width, uint height, int encodedBytes)
    {
        if (width == 0 || height == 0 || width > int.MaxValue || height > int.MaxValue)
            throw new TransferError(400, "invalid_image", "The PNG has invalid dimensions.");
        var pixels = (ulong)width * height;
        // Decoder, raster copy, PNG encoding and Windows clipboard allocations.
        // Use division before multiplication so forged IHDR dimensions cannot overflow.
        var available = (ulong)AvailableMemory;
        var encodedCopies = (ulong)encodedBytes * 8;
        if (encodedCopies >= available || pixels > (available - encodedCopies) / 20)
            throw InsufficientMemory();
    }

    public static TransferError InsufficientMemory() => new(503, "image_memory_unavailable",
        "The PC does not have enough available memory to process this image at full resolution. Close memory-heavy apps on the PC and retry. No resizing was performed.");
}
