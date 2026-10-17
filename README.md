# KizaTweaks by Qkiub

Optimizador de Windows 10/11 para juegos: detecta el hardware, recomienda lo que de verdad mejora el rendimiento,
aplica ajustes con un clic y **todo se puede deshacer**.

> Filosofía: solo lo que tiene efecto real o documentado. Lo popular pero dudoso va en «Experimental» con su advertencia,
> y el Benchmark integrado permite comprobar si sirve en cada equipo.

## Qué hace

| Página | Función |
|---|---|
| **Inicio** | Detecta CPU, GPU (VRAM real), RAM (velocidad actual vs. soportada → XMP/EXPO), monitor (Hz actual vs. máximo), discos (NVMe/SATA/HDD, salud, temperatura, desgaste), VBS/HVCI, HAGS, plan de energía. Genera recomendaciones ordenadas por impacto y un puntaje de configuración. |
| **Modo FPS** | Solo ajustes que dan rendimiento sin cambiar cómo funcionan tus apps y juegos, con un medidor de progreso y el nivel **Máximo rendimiento** de un clic. Interruptor por ajuste, revisión del PC (Hz del monitor, XMP/EXPO, driver de GPU, VBS, plan de energía), **Sesión de juego**, **GPU de alto rendimiento por juego** y **cierres recientes** (qué programa se cerró y qué falló, desde el visor de eventos). En portátil no fuerza el consumo máximo. |
| **Optimizar** | 72 ajustes (juegos, privacidad, IA, sistema, barra de tareas, Explorador, Windows Update, apariencia…). Cada uno muestra impacto, riesgo, si requiere reinicio y **exactamente qué cambia**. Perfiles: *Gamer recomendado*, *Máximo rendimiento*, *Gamer + privacidad máxima*, *Todo lo seguro*. |
| **Apps** | Quita 138 apps preinstaladas (Microsoft, juegos patrocinados, OEM de HP/Dell/Lenovo, OneDrive, Copilot…) con recomendación *seguro / opcional / no recomendado*. |
| **Procesos** | Lista con CPU y RAM reales, cerrar procesos (los críticos de Windows están protegidos), cambiar prioridad, **prioridad alta permanente** para un juego (IFEO PerfOptions) y **Sesión de juego**: cierra de un clic lo que corre de fondo. |
| **Arranque** | Activa o desactiva programas que inician con Windows, igual que el Administrador de tareas (reversible). |
| **Mantenimiento** | Comandos reales de Windows con consola en vivo: ReTRIM/optimizar unidades (`defrag`), revisar errores (`chkdsk /scan`), TRIM (`fsutil`), limpieza de temporales, reportes, caché de Windows Update y de distribución, componentes (`DISM /StartComponentCleanup`), shaders, papelera, DNS (`ipconfig /flushdns`), restablecer red (`netsh`), reparar Windows (`DISM /RestoreHealth`, `sfc /scannow`), diagnóstico de RAM, actualizar programas (`winget`) e informe de batería. Botones de **mantenimiento rápido** (semanal) y **completo** (mensual). |
| **Benchmark** | FPS promedio, 1 % low, 0,1 % low y frame time p99 con PresentMon. Mediciones del sistema en reposo (procesos, servicios, CPU, RAM, tiempo de arranque) y comparación antes/después. |
| **Respaldos** | Cada ajuste guarda el estado **anterior exacto** antes de cambiar nada. Deshacer uno o todo, crear punto de restauración, historial. |

### Lo que no hace (a propósito)
Desactivar Defender o Windows Update, quitar Edge, borrar el archivo de paginación, BCDEdit/HPET, desactivar servicios en masa,
«limpiar RAM», forzar prioridades de CPU o tiempos del sistema, ni cambios globales de DirectX o de la GPU (HAGS). Estos últimos
estuvieron en versiones anteriores y se retiraron porque hacían fallar apps y juegos en algunos equipos (Photoshop, FiveM):
siguen en el catálogo marcados como `retired` solo para deshacerlos automáticamente en quien ya los tenía. Están explicados en la página *Acerca de*.

## Descargar el .exe

Cada push a `main` compila en GitHub Actions y deja el ejecutable en **Actions → último run → Artifacts → KizaTweaks-win-x64**.
Al crear un tag `v1.0.0`, se publica un *Release* con el `.exe` adjunto.

El `.exe` es un solo archivo (~70 MB) que **no necesita instalar .NET**. Pide permisos de administrador al abrir.

## Generar el .exe en tu PC (lo más fácil)

Descomprime el proyecto en Windows y haz **doble clic en `compilar.bat`**. Instala .NET 8 si falta, descarga el medidor de FPS
(queda incluido dentro del .exe), corre las pruebas y deja `publish\KizaTweaks.exe` listo. La primera vez tarda 3 a 8 minutos.

## Compilar a mano

Requisitos: [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) (o Visual Studio 2022 con «Desarrollo de escritorio de .NET»).

```powershell
dotnet build KizaTweaks.sln -c Release
dotnet run --project tests/KizaTweaks.Tests -c Release            # pruebas unitarias
dotnet run --project tests/KizaTweaks.Tests -c Release -- --windows # pruebas contra Windows real
powershell -File tools/get-presentmon.ps1                          # medidor de FPS (opcional)
dotnet publish src/KizaTweaks.App -c Release -o publish            # genera publish\KizaTweaks.exe
```

Prueba automática de la interfaz (abre todas las páginas, detecta errores de enlace y guarda capturas):
`publish\KizaTweaks.exe --smoke-test` → resultado en `%ProgramData%\KizaTweaks\logs\smoke-test.txt` y capturas en
`logs\screens`. En GitHub Actions las capturas quedan en la rama `ci-screens`.

Los commits con `[preview]` en el mensaje compilan y prueban, pero no publican Release.

## Estructura

```
catalog/                 tweaks.json y apps.json (generados; se incrustan en el .exe)
tools/build_catalog.py   genera el catálogo (tweaks y apps) en español
src/KizaTweaks.Core      motor: tweaks, respaldos, hardware, procesos, apps, benchmark (sin UI)
src/KizaTweaks.Presentation  ViewModels (sin dependencia de WPF, se prueban en cualquier SO)
src/KizaTweaks.App       interfaz WPF, tema, manifiesto de administrador
tests/KizaTweaks.Tests   pruebas (unitarias con sistema simulado + integración con --windows)
```

### Actualizar el catálogo

```bash
python3 tools/build_catalog.py <carpeta-de-datos-base>
```

Para agregar un ajuste propio, edita la lista `KIZA` en `tools/build_catalog.py` (operaciones: `reg.set`, `reg.deleteValue`,
`reg.deleteKey`, `service`, `task`, `feature`, `env`, `dx.set`, `power.scheme`, `power.setting`, `cmd`). Para sumarlo a un
perfil, agrega su id a `PROFILE_GAMER`, `PROFILE_PERF` o `PROFILE_PRO`. Las pruebas verifican que **aplicar y
deshacer todo el catálogo deja el sistema idéntico**.

## Antes de distribuirlo públicamente

- **Firma de código**: sin firma, SmartScreen muestra «Editor desconocido» y algunos antivirus desconfían de cualquier
  optimizador. Opciones: Azure Trusted Signing (económico) o SignPath (gratis para proyectos open source). Hay un comentario
  en el workflow donde va el paso de firma.
- **Licencia**: decide la licencia de KizaTweaks. `THIRD-PARTY-NOTICES.md` es un aviso legal obligatorio que debe
  acompañar el repositorio; no aparece en la app.
- **Marca**: el logo (monograma KT con ケーティー) se genera con `python3 tools/make_logo.py` y el fondo con `python3 tools/make_background.py` (requieren Pillow). Si Qkiub tiene logo propio, reemplaza
  `logo.png` y `kizatweaks.ico` en `src/KizaTweaks.App/Assets`.

## Datos que guarda

Todo en `%ProgramData%\KizaTweaks`: `state\backups.json` (estado original de cada ajuste), `state\history.jsonl`,
`logs\`, `snapshots\` (mediciones) y `tools\` (PresentMon, si se descarga). No envía datos a ningún servidor.
