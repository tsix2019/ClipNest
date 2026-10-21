using System.Globalization;
using System.Runtime.InteropServices;

namespace ClipNest.Platform;

/// <summary>Installed font family names from DirectWrite's system collection (raw COM, AOT-friendly).</summary>
public static unsafe partial class FontList
{
    private static readonly Guid IID_IDWriteFactory = new("b859ee5a-d838-4b5b-a2e8-1adc7d93db48");

    [LibraryImport("dwrite.dll")]
    private static partial int DWriteCreateFactory(int factoryType, in Guid iid, out nint factory);

    /// <summary>
    /// Family names in the UI language when the font has one (e.g. "微软雅黑"), else English; sorted, no
    /// vertical "@" variants. Any of a family's names works as a XAML FontFamily. Empty on failure.
    /// </summary>
    public static IReadOnlyList<string> SystemFamilies()
    {
        var names = new SortedSet<string>(StringComparer.CurrentCultureIgnoreCase);
        nint factory = 0, collection = 0;
        try
        {
            if (DWriteCreateFactory(0 /* shared */, IID_IDWriteFactory, out factory) < 0) return [];
            // IDWriteFactory::GetSystemFontCollection (slot 3)
            if (Call(factory, 3, &collection, 0) < 0) return [];
            // IDWriteFontCollection::GetFontFamilyCount (slot 3)
            var count = ((delegate* unmanaged[Stdcall]<nint, uint>)VTable(collection)[3])(collection);
            var locale = CultureInfo.CurrentUICulture.Name;
            for (uint i = 0; i < count; i++)
            {
                nint family = 0, strings = 0;
                try
                {
                    // IDWriteFontCollection::GetFontFamily (slot 4); IDWriteFontFamily::GetFamilyNames (slot 6)
                    if (((delegate* unmanaged[Stdcall]<nint, uint, nint*, int>)VTable(collection)[4])(collection, i, &family) < 0) continue;
                    if (((delegate* unmanaged[Stdcall]<nint, nint*, int>)VTable(family)[6])(family, &strings) < 0) continue;
                    var name = PickName(strings, locale);
                    if (!string.IsNullOrWhiteSpace(name) && !name.StartsWith('@')) names.Add(name);
                }
                finally
                {
                    Release(strings);
                    Release(family);
                }
            }
        }
        catch (Exception e)
        {
            Log.Error("Listing fonts", e);
        }
        finally
        {
            Release(collection);
            Release(factory);
        }
        return [.. names];
    }

    /// <summary>IDWriteLocalizedStrings: the <paramref name="locale"/> name, else en-us, else the first.</summary>
    private static string? PickName(nint strings, string locale)
    {
        var vt = VTable(strings);
        uint index = 0;
        int exists = 0;
        // FindLocaleName (slot 4)
        var find = (delegate* unmanaged[Stdcall]<nint, char*, uint*, int*, int>)vt[4];
        fixed (char* l = locale)
            find(strings, l, &index, &exists);
        if (exists == 0)
            fixed (char* en = "en-us")
                find(strings, en, &index, &exists);
        if (exists == 0) index = 0;

        uint length = 0;
        // GetStringLength (slot 7), GetString (slot 8)
        if (((delegate* unmanaged[Stdcall]<nint, uint, uint*, int>)vt[7])(strings, index, &length) < 0) return null;
        var buffer = new char[length + 1];
        fixed (char* b = buffer)
        {
            if (((delegate* unmanaged[Stdcall]<nint, uint, char*, uint, int>)vt[8])(strings, index, b, length + 1) < 0) return null;
        }
        return new string(buffer, 0, (int)length);
    }

    private static nint* VTable(nint obj) => *(nint**)obj;

    private static int Call(nint obj, int slot, nint* result, int flag) =>
        ((delegate* unmanaged[Stdcall]<nint, nint*, int, int>)VTable(obj)[slot])(obj, result, flag);

    private static void Release(nint obj)
    {
        if (obj != 0) ((delegate* unmanaged[Stdcall]<nint, uint>)VTable(obj)[2])(obj);
    }
}
