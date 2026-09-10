#pragma once
// Cerrar en memoria la ruta del driver hacia el rasgo 18 (neural rendering).
//
// Portado de dlss5-bridge (NIGos), MIT. Ver licenses/DLSS5-Bridge-MIT.txt y NOTICE.
// El diagnóstico y el mecanismo son suyos; aquí solo se reimplementa para el addon
// del feeder, que es el que carga en CS1.
//
// El problema: desde 32.0.16.1664 el _nvngx.dll del driver enruta el rasgo 18 hacia
// nvngx_dlssnr.dll por su cuenta. Con el snippet 310.8.0.0 eso entrega a D3D12 un
// descriptor heap cuya descripción se lee corrupta, y D3D12 revienta dentro de
// SetDescriptorHeaps. Lo hemos reproducido tres veces en el fixture con RenoDX 4.55,
// 4.60 y 4.70 sobre los drivers 616.86 y 616.92, con la pila
// D3D12Core.dll <- nvngx_dlssnr.dll <- _nvngx.dll <- renodx-dlss5.addon64.
//
// El arreglo: _nvngx.dll lleva una tabla de 19 entradas «id de rasgo -> nombre de
// snippet» en datos escribibles. Las entradas 0..17 son idénticas entre 1656 y 1664;
// la 18 es la única diferencia — 1656 apunta a L"" y 1664 a L"dlssnr". Con el nombre
// vacío, NGXSecureLoadFeature no carga nada y devuelve 0xBAD00001, que es justo lo
// que hacía 1656. El consumidor vuelve a conducir el rasgo 18 él mismo, que es el
// camino que funciona.
//
// Deliberadamente estrecho. La tabla se localiza por patrón, nunca por dirección: la
// entrada 1 debe leer "dlss", la 11 "dlssg" y la 18 "dlssnr". Un driver que la
// reordene, la mueva o la haga crecer simplemente no encaja, y esto no hace nada.
// No se toca ningún archivo en disco y ningún otro rasgo.

#include <windows.h>
#include <cstddef>
#include <cwchar>

enum NeuralFxNgxRoute
{
    NFX_NGX_ROUTE_UNTOUCHED = 0,   // no había tabla que encajara: el driver conserva su ruta
    NFX_NGX_ROUTE_CLOSED    = 1,   // entrada 18 vaciada en este proceso
    NFX_NGX_ROUTE_NO_LOADER = 2,   // no hay _nvngx.dll cargado ni registrado
    NFX_NGX_ROUTE_LOCKED    = 3,   // la tabla existe pero no se pudo hacer escribible
};

static bool NeuralFxNgxNameIs(const BYTE *base, size_t image, const void *entry, const wchar_t *want)
{
    const BYTE *s = static_cast<const BYTE *>(entry);
    if (s < base || s >= base + image) return false;
    const size_t room = static_cast<size_t>(base + image - s) / sizeof(wchar_t);
    const size_t n = wcslen(want);
    if (room <= n) return false;
    return wcsncmp(reinterpret_cast<const wchar_t *>(s), want, n + 1) == 0;
}

// Devuelve la dirección de la tabla encontrada, o nullptr.
static void **NeuralFxNgxFindFeatureTable(HMODULE ngx, size_t *image_out)
{
    BYTE *base = reinterpret_cast<BYTE *>(ngx);
    if (base == nullptr) return nullptr;
    const IMAGE_DOS_HEADER *dos = reinterpret_cast<const IMAGE_DOS_HEADER *>(base);
    if (dos->e_magic != IMAGE_DOS_SIGNATURE) return nullptr;
    const IMAGE_NT_HEADERS *nt = reinterpret_cast<const IMAGE_NT_HEADERS *>(base + dos->e_lfanew);
    if (nt->Signature != IMAGE_NT_SIGNATURE) return nullptr;

    const size_t image = nt->OptionalHeader.SizeOfImage;
    if (image_out) *image_out = image;
    const IMAGE_SECTION_HEADER *sec = IMAGE_FIRST_SECTION(nt);
    for (WORD i = 0; i < nt->FileHeader.NumberOfSections; ++i)
    {
        if ((sec[i].Characteristics & IMAGE_SCN_MEM_WRITE) == 0) continue;
        void **p = reinterpret_cast<void **>(base + sec[i].VirtualAddress);
        void **end = reinterpret_cast<void **>(base + sec[i].VirtualAddress + sec[i].Misc.VirtualSize);
        for (; p + 19 <= end; ++p)
        {
            if (!NeuralFxNgxNameIs(base, image, p[1], L"dlss")) continue;
            if (!NeuralFxNgxNameIs(base, image, p[11], L"dlssg")) continue;
            if (!NeuralFxNgxNameIs(base, image, p[18], L"dlssnr")) continue;
            return p;
        }
    }
    return nullptr;
}

// Donde el proceso encontraría NGX si nadie interfiriera. El propio SDK de NVIDIA
// lee esta clave, así que nombra el loader que el juego va a recibir.
static bool NeuralFxNgxLoaderPath(wchar_t *out, size_t cch)
{
    wchar_t dir[MAX_PATH] = {};
    DWORD cb = sizeof(dir);
    if (RegGetValueW(HKEY_LOCAL_MACHINE, L"SOFTWARE\\NVIDIA Corporation\\Global\\NGXCore",
                     L"FullPath", RRF_RT_REG_SZ, nullptr, dir, &cb) != ERROR_SUCCESS || dir[0] == 0)
        return false;
    _snwprintf_s(out, cch, _TRUNCATE, L"%ls\\_nvngx.dll", dir);
    return GetFileAttributesW(out) != INVALID_FILE_ATTRIBUTES;
}

/// <summary>
/// Cierra la ruta del driver al rasgo 18 en este proceso. Idempotente y sin efectos
/// en disco. Nunca lanza; ante cualquier duda deja el driver como está.
/// </summary>
static NeuralFxNgxRoute NeuralFxCloseDriverNeuralRoute(void **where)
{
    if (where) *where = nullptr;
    HMODULE ngx = GetModuleHandleW(L"_nvngx.dll");
    wchar_t path[MAX_PATH] = {};
    // No se llama desde DllMain: aquí un LoadLibrary es seguro.
    if (ngx == nullptr && NeuralFxNgxLoaderPath(path, MAX_PATH)) ngx = LoadLibraryW(path);
    if (ngx == nullptr) return NFX_NGX_ROUTE_NO_LOADER;

    size_t image = 0;
    void **table = NeuralFxNgxFindFeatureTable(ngx, &image);
    if (table == nullptr) return NFX_NGX_ROUTE_UNTOUCHED;
    if (where) *where = table;

    DWORD old = 0;
    if (!VirtualProtect(&table[18], sizeof(void *), PAGE_READWRITE, &old)) return NFX_NGX_ROUTE_LOCKED;
    // La cadena vacía tiene que vivir dentro del loader, no en este addon: ReShade
    // puede descargar un addon con el proceso en marcha y el loader sigue mapeado,
    // así que un puntero a nuestro .rdata quedaría colgando. El terminador del
    // propio "dlssnr" del loader es una cadena vacía en una dirección permanente, y
    // NeuralFxNgxNameIs acaba de probar que está dentro de la imagen.
    table[18] = static_cast<wchar_t *>(table[18]) + wcslen(L"dlssnr");
    VirtualProtect(&table[18], sizeof(void *), old, &old);
    return NFX_NGX_ROUTE_CLOSED;
}
