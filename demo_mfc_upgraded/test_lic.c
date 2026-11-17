#include <windows.h>
#include <stdio.h>

typedef int (*fn_init_mwwz_lic)(const char* lic_path);
typedef int (*fn_init_mwwz)();

int main() {
    HMODULE hMwwz = LoadLibraryA("mwwz.dll");
    if (!hMwwz) {
        printf("Failed to load mwwz.dll, error: %lu\n", GetLastError());
        return 1;
    }
    printf("mwwz.dll loaded successfully at %p!\n", hMwwz);

    fn_init_mwwz_lic init_lic = (fn_init_mwwz_lic)GetProcAddress(hMwwz, "init_mwwz_lic");
    if (init_lic) {
        int ret = init_lic("license.lic");
        printf("init_mwwz_lic('license.lic') returned: %d\n", ret);
    }

    fn_init_mwwz init_func = (fn_init_mwwz)GetProcAddress(hMwwz, "init_mwwz");
    if (init_func) {
        int ret = init_func();
        printf("init_mwwz() returned: %d\n", ret);
    }

    return 0;
}
