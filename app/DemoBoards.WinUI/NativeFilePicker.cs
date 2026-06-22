using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Windows.Storage;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace DemoBoards_WinUI;

public sealed record NativeAttachmentFile(
    string Name,
    string ContentType,
    byte[] Bytes,
    long Size);

public static class NativeFilePicker
{
    public static async Task<string?> PickJsonTextAsync()
    {
        StorageFile? file = await PickSingleJsonFileAsync();
        if (file is null)
        {
            return null;
        }

        return await FileIO.ReadTextAsync(file);
    }

    public static async Task<bool> SaveJsonTextAsync(string suggestedFileName, string jsonText)
    {
        FileSavePicker picker = CreateSavePicker();
        picker.SuggestedFileName = string.IsNullOrWhiteSpace(suggestedFileName) ? "board-runtime-dump" : Path.GetFileNameWithoutExtension(suggestedFileName);
        StorageFile? file = await picker.PickSaveFileAsync();
        if (file is null)
        {
            return false;
        }

        await FileIO.WriteTextAsync(file, jsonText ?? string.Empty, Windows.Storage.Streams.UnicodeEncoding.Utf8);
        return true;
    }

    public static async Task<NativeAttachmentFile?> PickSingleAttachmentAsync()
    {
        IReadOnlyList<NativeAttachmentFile> files = await PickMultipleAttachmentsAsync(false);
        return files.FirstOrDefault();
    }

    public static Task<IReadOnlyList<NativeAttachmentFile>> ReadAttachmentsAsync(IReadOnlyList<IStorageItem> items)
    {
        return ReadAttachmentsInternalAsync(items ?? Array.Empty<IStorageItem>());
    }

    public static async Task<IReadOnlyList<NativeAttachmentFile>> PickMultipleAttachmentsAsync(bool allowMultiple = true)
    {
        FileOpenPicker picker = CreatePicker();
        if (!allowMultiple)
        {
            StorageFile? file = await picker.PickSingleFileAsync();
            if (file is null)
            {
                return Array.Empty<NativeAttachmentFile>();
            }

            NativeAttachmentFile attachment = await ReadAttachmentAsync(file);
            return new[] { attachment };
        }

        IReadOnlyList<StorageFile> files = await picker.PickMultipleFilesAsync();
        if (files.Count == 0)
        {
            return Array.Empty<NativeAttachmentFile>();
        }

        var attachments = new List<NativeAttachmentFile>(files.Count);
        foreach (StorageFile file in files)
        {
            attachments.Add(await ReadAttachmentAsync(file));
        }

        return attachments;
    }

    private static FileOpenPicker CreatePicker()
    {
        var picker = new FileOpenPicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            ViewMode = PickerViewMode.List
        };
        picker.FileTypeFilter.Add("*");
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(((App)Application.Current).MainWindow));
        return picker;
    }

    private static async Task<StorageFile?> PickSingleJsonFileAsync()
    {
        FileOpenPicker picker = CreatePicker();
        picker.FileTypeFilter.Clear();
        picker.FileTypeFilter.Add(".json");
        return await picker.PickSingleFileAsync();
    }

    private static FileSavePicker CreateSavePicker()
    {
        var picker = new FileSavePicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            DefaultFileExtension = ".json"
        };
        picker.FileTypeChoices.Add("JSON", new List<string> { ".json" });
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(((App)Application.Current).MainWindow));
        return picker;
    }

    private static async Task<NativeAttachmentFile> ReadAttachmentAsync(StorageFile file)
    {
        using Stream stream = await file.OpenStreamForReadAsync();
        byte[] bytes = new byte[stream.Length];
        int offset = 0;
        while (offset < bytes.Length)
        {
            int read = await stream.ReadAsync(bytes, offset, bytes.Length - offset).ConfigureAwait(false);
            if (read <= 0)
            {
                break;
            }

            offset += read;
        }

        return new NativeAttachmentFile(
            string.IsNullOrWhiteSpace(file.Name) ? "upload.bin" : file.Name,
            string.IsNullOrWhiteSpace(file.ContentType) ? "application/octet-stream" : file.ContentType,
            bytes,
            bytes.LongLength);
    }

    private static async Task<IReadOnlyList<NativeAttachmentFile>> ReadAttachmentsInternalAsync(IReadOnlyList<IStorageItem> items)
    {
        if (items.Count == 0)
        {
            return Array.Empty<NativeAttachmentFile>();
        }

        var attachments = new List<NativeAttachmentFile>(items.Count);
        foreach (IStorageItem item in items)
        {
            if (item is StorageFile file)
            {
                attachments.Add(await ReadAttachmentAsync(file));
            }
        }

        return attachments;
    }
}