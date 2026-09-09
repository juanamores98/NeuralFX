# NeuralFX — continuidad de la consolidación

- Baseline: `a063036271f2b84d0abeca31153400940190dbf6`. Trabajo: rama `codex/neuralfx-consolidation`, integración final en `main`. Obtén el HEAD exacto con `git rev-parse HEAD`; el manifiesto de cada build lo incorpora.
- Cambios: ABI 3/build 4, decoder seguro, descriptor MV, recursos propios experimentales, Off/restauración, controles de sesión, IPC 4, UI y confianza. Detalle y estado de los 15 cortes: [CONSOLIDACION-2026-09.md](docs/CONSOLIDACION-2026-09.md).
- Último código validado: `bd2dfefc00c295a3bbf354d2ff4b5439ab3d5616`; [CI 34299125908](https://github.com/juanamores98/NeuralFX/actions/runs/34299125908) aprobado: 76 casos Hub/IPC, mod completo con dobles, WARP y ASan/UBSan. Sin CS1 ni NVIDIA NR. No trasladar las pruebas históricas del bridge `.3` a esta build.
- Pendiente: entradas completas color/depth/MV de la misma cámara, pre-UI con prueba de identidad, prepare/fallback temporal, confirmación NR y SR interno. Jitter y capacidades no acreditadas permanecen desarmadas.
- Siguiente paso: compilar con los ensamblados reales mediante `tools/Build-NeuralFX.ps1`; probar óptico 100% en ciudad y exportar traza de cámaras/tuple. La traza decide el hook pre-UI; el consumidor requiere un contrato NR verificable.
- No modificar los otros FX, no sustituir iluminación/tono/niebla, no renombrar RR como NR. No desplegar binarios cargados; actualización conjunta y backup con el instalador.
