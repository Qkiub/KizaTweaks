#!/usr/bin/env python3
"""
Genera el catálogo de KizaTweaks a partir de Win11Debloat (MIT, Raphire)
más los tweaks propios de KizaTweaks.

Uso:
    git clone --depth 1 https://github.com/Raphire/Win11Debloat.git /ruta/w11d
    python3 tools/build_catalog.py /ruta/w11d

Escribe:
    catalog/tweaks.json  -> todos los tweaks (Win11Debloat + KizaTweaks)
    catalog/apps.json    -> apps removibles con descripción en español
"""
import json
import os
import re
import subprocess
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT_DIR = os.path.join(ROOT, "catalog")

HIVES = {
    "HKEY_CURRENT_USER": "HKCU",
    "HKEY_LOCAL_MACHINE": "HKLM",
    "HKEY_CLASSES_ROOT": "HKCR",
    "HKEY_USERS": "HKU",
}

# --------------------------------------------------------------------------
# Parser de archivos .reg (formato "Windows Registry Editor Version 5.00")
# --------------------------------------------------------------------------

def read_reg(path):
    raw = open(path, "rb").read()
    if raw.startswith(b"\xff\xfe") or raw.startswith(b"\xfe\xff"):
        text = raw.decode("utf-16")
    else:
        text = raw.decode("utf-8-sig")
    return parse_reg(text)


def _split_hive(full):
    hive_long, _, sub = full.partition("\\")
    if hive_long not in HIVES:
        raise ValueError(f"Hive no soportado: {hive_long}")
    return HIVES[hive_long], sub


def _unescape(s):
    return s.replace('\\"', '"').replace("\\\\", "\\")


def parse_reg(text):
    # Une líneas continuadas con "\" (valores hex largos)
    lines = []
    buf = ""
    for line in text.replace("\r", "").split("\n"):
        line = line.rstrip()
        if buf:
            line = buf + line.lstrip()
            buf = ""
        if line.endswith("\\") and not line.startswith(";"):
            buf = line[:-1]
            continue
        lines.append(line)

    ops = []
    hive = path = None
    for line in lines:
        s = line.strip()
        if not s or s.startswith(";") or s.startswith("Windows Registry Editor"):
            continue
        if s.startswith("[") and s.endswith("]"):
            inner = s[1:-1]
            if inner.startswith("-"):
                h, p = _split_hive(inner[1:])
                ops.append({"op": "reg.deleteKey", "hive": h, "path": p})
                hive = path = None
            else:
                hive, path = _split_hive(inner)
                ops.append({"op": "reg.ensureKey", "hive": hive, "path": path})
            continue
        if hive is None:
            raise ValueError(f"Valor fuera de clave: {s}")
        m = re.match(r'^(@|"((?:[^"\\]|\\.)*)")=(.*)$', s)
        if not m:
            raise ValueError(f"Línea no reconocida: {s}")
        name = "" if m.group(1) == "@" else _unescape(m.group(2))
        data = m.group(3)
        base = {"hive": hive, "path": path, "name": name}
        if data == "-":
            ops.append({"op": "reg.deleteValue", **base})
        elif data.startswith('"'):
            ops.append({"op": "reg.set", **base, "type": "String",
                        "value": _unescape(data[1:-1])})
        elif data.startswith("dword:"):
            ops.append({"op": "reg.set", **base, "type": "DWord",
                        "value": int(data[6:], 16)})
        elif data.startswith("qword:"):
            ops.append({"op": "reg.set", **base, "type": "QWord",
                        "value": int(data[6:], 16)})
        else:
            hm = re.match(r"^hex(?:\(([0-9a-fA-F]+)\))?:(.*)$", data)
            if not hm:
                raise ValueError(f"Tipo no soportado: {s}")
            kind = (hm.group(1) or "3").lower()
            body = hm.group(2).replace(" ", "")
            bytes_ = [int(b, 16) for b in body.split(",") if b]
            typemap = {"3": "Binary", "2": "ExpandString", "7": "MultiString",
                       "b": "QWord", "4": "DWord", "0": "None"}
            t = typemap.get(kind)
            if t is None:
                raise ValueError(f"hex({kind}) no soportado: {s}")
            if t == "Binary" or t == "None":
                ops.append({"op": "reg.set", **base, "type": "Binary", "value": bytes_})
            elif t == "ExpandString":
                val = bytes(bytes_).decode("utf-16-le").rstrip("\x00")
                ops.append({"op": "reg.set", **base, "type": "ExpandString", "value": val})
            elif t == "MultiString":
                val = bytes(bytes_).decode("utf-16-le").rstrip("\x00").split("\x00")
                ops.append({"op": "reg.set", **base, "type": "MultiString", "value": val})
            elif t == "QWord":
                ops.append({"op": "reg.set", **base, "type": "QWord",
                            "value": int.from_bytes(bytes(bytes_), "little")})
            elif t == "DWord":
                ops.append({"op": "reg.set", **base, "type": "DWord",
                            "value": int.from_bytes(bytes(bytes_), "little")})
    # Quita "ensureKey" redundantes: el motor crea la clave al escribir un valor.
    keys_with_values = {(o["hive"], o["path"]) for o in ops if o["op"] == "reg.set"}
    return [o for o in ops if not (o["op"] == "reg.ensureKey"
                                   and (o["hive"], o["path"]) in keys_with_values)]


# --------------------------------------------------------------------------
# Textos en español para los tweaks que vienen de Win11Debloat
# id: (nombre, qué hace, impacto, riesgo, nota opcional)
# impacto: fps | latency | privacy | cleanup | appearance | storage | stability
# riesgo:  safe | moderate | risky
# --------------------------------------------------------------------------

ES = {
    "DisableTelemetry": ("Desactivar telemetría y anuncios personalizados",
        "Reduce los datos de diagnóstico al mínimo, desactiva el ID de publicidad, el seguimiento de apps abiertas, el historial de actividad y las encuestas de opinión.",
        "privacy", "safe", None),
    "DisableSuggestions": ("Quitar tips y contenido sugerido",
        "Deja de mostrar sugerencias, apps promocionadas y 'consejos' en Inicio, Configuración y el Explorador.",
        "cleanup", "safe", None),
    "DisableNotifications": ("Desactivar todas las notificaciones",
        "Bloquea las notificaciones de apps y del sistema.",
        "cleanup", "moderate", "También ocultará avisos útiles (Discord, Steam, antivirus)."),
    "DisableLocationServices": ("Desactivar ubicación",
        "Apaga el servicio de ubicación de Windows y el acceso de las apps a tu ubicación.",
        "privacy", "safe", "Apps como Clima o Mapas dejarán de detectar dónde estás."),
    "DisableFindMyDevice": ("Desactivar 'Encontrar mi dispositivo'",
        "Windows deja de registrar la ubicación del equipo para localizarlo si se pierde.",
        "privacy", "safe", None),
    "DisableLockscreenTips": ("Quitar tips de la pantalla de bloqueo",
        "Elimina datos curiosos, sugerencias y anuncios de la pantalla de bloqueo.",
        "cleanup", "safe", None),
    "DisableDesktopSpotlight": ("Desactivar fondo Windows Spotlight",
        "Quita la opción de fondo de escritorio que cambia sola con imágenes de Bing.",
        "appearance", "safe", None),
    "HideDesktopSpotlightIcon": ("Ocultar 'Más información sobre esta imagen'",
        "Quita el ícono del escritorio que aparece con el fondo Spotlight.",
        "appearance", "safe", None),
    "DisableEdgeAds": ("Quitar anuncios y noticias de Edge",
        "Desactiva sugerencias, contenido patrocinado y el feed de noticias en Microsoft Edge.",
        "cleanup", "safe", None),
    "DisableCopilot": ("Desactivar Copilot",
        "Apaga Microsoft Copilot en Windows y quita su botón de la barra de tareas.",
        "privacy", "safe", None),
    "DisableRecall": ("Desactivar Recall",
        "Impide que Windows tome capturas periódicas de tu pantalla para la búsqueda con IA.",
        "privacy", "safe", None),
    "DisableClickToDo": ("Desactivar Click to Do",
        "Apaga el análisis con IA de texto e imágenes en pantalla.",
        "privacy", "safe", None),
    "DisableAISvcAutoStart": ("Evitar que el servicio de IA arranque solo",
        "El servicio WSAIFabricSvc pasa a inicio manual; deja de consumir recursos al arrancar.",
        "fps", "safe", None),
    "DisableDVR": ("Desactivar grabación en segundo plano",
        "Apaga la grabación de juego/pantalla de Xbox Game Bar, que puede restar FPS mientras juegas.",
        "fps", "safe", "Perderás la función de grabar clips con Win+Alt+G."),
    "DisableGameBarIntegration": ("Desactivar integración de Game Bar",
        "El botón Xbox del mando deja de abrir Game Bar y se bloquean sus ventanas emergentes.",
        "cleanup", "moderate", "Si usas el overlay de Game Bar (FPS, chat de Xbox), no lo apliques."),
    "DisableStartRecommended": ("Ocultar 'Recomendado' en Inicio",
        "Quita la sección de archivos y apps recomendadas del menú Inicio.",
        "cleanup", "safe", None),
    "DisableStartPhoneLink": ("Quitar Vínculo móvil del menú Inicio",
        "Oculta el panel del teléfono conectado en el menú Inicio.",
        "cleanup", "safe", None),
    "DisableBing": ("Búsqueda sin Bing ni web",
        "El menú Inicio busca solo en tu PC: sin resultados de Bing ni Copilot.",
        "privacy", "safe", None),
    "DisableSettings365Ads": ("Quitar anuncios de Microsoft 365 en Configuración",
        "Oculta las promociones de Microsoft 365 en la página de inicio de Configuración.",
        "cleanup", "safe", None),
    "DisableSettingsHome": ("Ocultar página 'Inicio' de Configuración",
        "Configuración abre directo en Sistema en lugar de la página de inicio.",
        "appearance", "safe", None),
    "DisableEdgeAI": ("Desactivar IA en Edge",
        "Apaga Copilot y funciones de IA dentro de Microsoft Edge.",
        "privacy", "safe", None),
    "DisableChromeAI": ("Desactivar IA en Chrome",
        "Apaga las funciones de IA de Google Chrome mediante políticas.",
        "privacy", "safe", None),
    "DisablePaintAI": ("Desactivar IA en Paint",
        "Apaga Cocreator, relleno generativo y otras funciones de IA de Paint.",
        "privacy", "safe", None),
    "DisableNotepadAI": ("Desactivar IA en el Bloc de notas",
        "Apaga las funciones de reescritura con IA del Bloc de notas.",
        "privacy", "safe", None),
    "EnableDarkMode": ("Modo oscuro",
        "Activa el tema oscuro para Windows y las apps.",
        "appearance", "safe", None),
    "DisableDragTray": ("Desactivar bandeja al arrastrar archivos",
        "Quita la bandeja de compartir que aparece arriba al arrastrar archivos.",
        "appearance", "safe", None),
    "RevertContextMenu": ("Menú contextual clásico",
        "El clic derecho vuelve a mostrar todas las opciones, sin 'Mostrar más opciones'.",
        "appearance", "safe", None),
    "DisableMouseAcceleration": ("Desactivar aceleración del mouse",
        "Apaga 'Mejorar precisión del puntero': el movimiento del cursor es 1:1 con la mano, clave para apuntar en shooters.",
        "latency", "safe", None),
    "DisableStickyKeys": ("Desactivar atajo de Teclas especiales",
        "Presionar Shift 5 veces ya no abre el aviso de Teclas especiales en medio del juego.",
        "fps", "safe", None),
    "DisableWindowSnapping": ("Desactivar ajuste de ventanas",
        "Las ventanas dejan de acomodarse solas al arrastrarlas a los bordes.",
        "appearance", "moderate", None),
    "DisableSnapAssist": ("Desactivar sugerencias de Snap",
        "Al acomodar una ventana ya no sugiere qué app poner al lado.",
        "appearance", "safe", None),
    "DisableSnapLayouts": ("Ocultar diseños de Snap",
        "Quita el menú de diseños que aparece al pasar el mouse por el botón maximizar.",
        "appearance", "safe", None),
    "HideTabsInAltTab": ("Ocultar pestañas de Edge en Alt+Tab",
        "Alt+Tab muestra solo ventanas, no las pestañas del navegador.",
        "appearance", "safe", None),
    "TaskbarAlignLeft": ("Barra de tareas a la izquierda",
        "Alinea los íconos de la barra de tareas a la izquierda, como en Windows 10.",
        "appearance", "safe", None),
    "HideSearchTb": ("Ocultar búsqueda de la barra de tareas",
        "Quita el cuadro de búsqueda; puedes seguir buscando con la tecla Windows.",
        "appearance", "safe", None),
    "HideTaskview": ("Ocultar botón Vista de tareas",
        "Quita el botón de escritorios virtuales de la barra de tareas.",
        "appearance", "safe", None),
    "HideChat": ("Ocultar Chat de la barra de tareas",
        "Quita el ícono de Chat/Teams de la barra de tareas.",
        "appearance", "safe", None),
    "DisableStorageSense": ("Desactivar Sensor de almacenamiento",
        "Windows deja de borrar automáticamente temporales y la papelera.",
        "storage", "moderate", "No recomendado si tu disco del sistema es pequeño (128 GB o menos)."),
    "DisableFastStartup": ("Desactivar Inicio rápido",
        "Apagar el PC lo apaga de verdad. Evita problemas con drivers y actualizaciones que no se aplican.",
        "stability", "safe", "El arranque puede tardar 1-3 segundos más."),
    "DisableBitlockerAutoEncryption": ("Evitar cifrado automático de BitLocker",
        "Windows no cifrará el disco por su cuenta. No descifra un disco que ya esté cifrado.",
        "storage", "moderate", "Si tu disco ya está cifrado, guarda tu clave de recuperación."),
    "DisableModernStandbyNetworking": ("Sin red en suspensión moderna",
        "El portátil deja de conectarse a internet mientras está suspendido; ahorra batería.",
        "storage", "safe", None),
    "EnableEndTask": ("'Finalizar tarea' en la barra de tareas",
        "Agrega 'Finalizar tarea' al clic derecho de las apps: cierra juegos congelados al instante.",
        "stability", "safe", None),
    "EnableLastActiveClick": ("Clic alterna entre ventanas de la misma app",
        "Hacer clic varias veces en el ícono de una app cambia entre sus ventanas abiertas.",
        "appearance", "safe", None),
    "ExplorerToThisPC": ("Explorador abre en 'Este equipo'",
        "El Explorador de archivos abre mostrando tus discos en lugar de Inicio.",
        "appearance", "safe", None),
    "ShowKnownFileExt": ("Mostrar extensiones de archivo",
        "Muestra .exe, .zip, .pdf, etc. Ayuda a detectar archivos falsos como 'foto.jpg.exe'.",
        "stability", "safe", None),
    "ShowHiddenFolders": ("Mostrar archivos ocultos",
        "Muestra archivos, carpetas y unidades ocultas.",
        "appearance", "safe", None),
    "HideDupliDrive": ("Ocultar unidades extraíbles duplicadas",
        "Las USB aparecen solo bajo 'Este equipo' en el panel lateral.",
        "appearance", "safe", None),
    "HideHome": ("Ocultar 'Inicio' del Explorador",
        "Quita 'Inicio' del panel lateral del Explorador.",
        "appearance", "safe", None),
    "HideGallery": ("Ocultar 'Galería' del Explorador",
        "Quita 'Galería' del panel lateral del Explorador.",
        "appearance", "safe", None),
    "DisableTransparency": ("Desactivar transparencias",
        "Quita el efecto translúcido de menús y barra de tareas. Ahorro mínimo de GPU.",
        "appearance", "safe", None),
    "DisableAnimations": ("Desactivar animaciones",
        "Quita animaciones y efectos visuales; Windows se siente más inmediato.",
        "appearance", "safe", None),
    "DisableUpdateASAP": ("No recibir actualizaciones apenas salen",
        "Windows deja de instalar las actualizaciones opcionales en cuanto están disponibles.",
        "stability", "safe", None),
    "PreventUpdateAutoReboot": ("Sin reinicios automáticos por actualizaciones",
        "Windows no reiniciará solo mientras tengas la sesión iniciada.",
        "stability", "safe", None),
    "DisableDeliveryOptimization": ("No compartir actualizaciones con otros PCs",
        "Desactiva la Optimización de distribución: tu internet no se usa para enviar actualizaciones a otros.",
        "latency", "safe", None),
    "DisableDeviceAutoAppDownload": ("Sin apps automáticas de dispositivos",
        "Evita que Windows instale solo apps de fabricantes al conectar un monitor, mouse, etc.",
        "cleanup", "safe", None),
    "HideOnedrive": ("Ocultar OneDrive del Explorador",
        "Quita OneDrive del panel lateral del Explorador.",
        "appearance", "safe", None),
    "Hide3dObjects": ("Ocultar 'Objetos 3D'",
        "Quita la carpeta Objetos 3D de 'Este equipo' (Windows 10).",
        "appearance", "safe", None),
    "HideMusic": ("Ocultar carpeta 'Música'",
        "Quita la carpeta Música de 'Este equipo' (Windows 10).",
        "appearance", "safe", None),
    "AddFoldersToThisPC": ("Carpetas comunes en 'Este equipo'",
        "Vuelve a mostrar Escritorio, Descargas, Documentos, etc. en 'Este equipo'.",
        "appearance", "safe", None),
    "HideIncludeInLibrary": ("Quitar 'Incluir en biblioteca' del menú",
        "Limpia el menú contextual (Windows 10).",
        "appearance", "safe", None),
    "HideGiveAccessTo": ("Quitar 'Conceder acceso a' del menú",
        "Limpia el menú contextual (Windows 10).",
        "appearance", "safe", None),
    "HideShare": ("Quitar 'Compartir' del menú",
        "Limpia el menú contextual (Windows 10).",
        "appearance", "safe", None),
    "DisableBraveBloat": ("Limpiar Brave",
        "Desactiva IA, cripto, noticias y VPN promocionada en el navegador Brave.",
        "cleanup", "safe", None),
}

# Tweaks de grupos de opción múltiple que expongo como interruptor, con su undo.
EXTRA_UNDO = {
    "HideTabsInAltTab": "Show_3_Tabs_In_Alt_Tab.reg",
}

CATEGORY_MAP = {
    "Privacy & Suggested Content": "privacy",
    "System": "system",
    "Start Menu & Search": "start",
    "AI": "ai",
    "Windows Update": "update",
    "Taskbar": "taskbar",
    "Appearance": "appearance",
    "File Explorer": "explorer",
    "Gaming": "gaming",
    "Multi-tasking": "multitasking",
    "Other": "other",
}

# Recomendados por perfil (ids)
PROFILE_GAMER = {
    "DisableTelemetry", "DisableSuggestions", "DisableLockscreenTips", "DisableEdgeAds",
    "DisableCopilot", "DisableRecall", "DisableClickToDo", "DisableAISvcAutoStart",
    "DisableDVR", "DisableStartRecommended", "DisableStartPhoneLink", "DisableBing",
    "DisableSettings365Ads", "DisableMouseAcceleration", "DisableStickyKeys",
    "DisableFastStartup", "EnableEndTask", "ShowKnownFileExt", "DisableDeliveryOptimization",
    "DisableDeviceAutoAppDownload", "PreventUpdateAutoReboot", "RevertContextMenu",
}
PROFILE_PRIVACY = PROFILE_GAMER | {
    "DisableLocationServices", "DisableFindMyDevice", "DisableEdgeAI", "DisableChromeAI",
    "DisablePaintAI", "DisableNotepadAI", "HideOnedrive",
}


def resolve_reg(regdir, name):
    for cand in (os.path.join(regdir, "Undo", name), os.path.join(regdir, name)):
        if os.path.exists(cand):
            return cand
    # Algunos nombres difieren en mayúsculas (Show_Onedrive_folder.reg)
    for sub in ("Undo", ""):
        d = os.path.join(regdir, sub)
        for f in os.listdir(d):
            if f.lower() == name.lower():
                return os.path.join(d, f)
    return None


def w11d_tweaks(w11d):
    feats = json.load(open(os.path.join(w11d, "Config", "Features.json"), encoding="utf-8-sig"))
    regdir = os.path.join(w11d, "Regfiles")
    out = []
    for f in feats["Features"]:
        fid = f["FeatureId"]
        if fid not in ES or not f.get("RegistryKey"):
            continue
        apply_path = resolve_reg(regdir, f["RegistryKey"])
        undo_name = f.get("RegistryUndoKey") or EXTRA_UNDO.get(fid)
        undo_path = resolve_reg(regdir, undo_name) if undo_name else None
        if not apply_path or not undo_path:
            print(f"  ! omitido {fid}: falta .reg ({f['RegistryKey']} / {undo_name})")
            continue
        name, desc, impact, risk, note = ES[fid]
        profiles = []
        if fid in PROFILE_GAMER:
            profiles.append("gamer")
        if fid in PROFILE_PRIVACY:
            profiles.append("privacy")
        out.append({
            "id": "q." + fid,
            "name": name,
            "description": desc,
            "note": note,
            "category": CATEGORY_MAP.get(f.get("Category"), "other"),
            "impact": impact,
            "risk": risk,
            "requiresReboot": bool(f.get("RequiresReboot")),
            "restartExplorer": f.get("Category") in ("Taskbar", "File Explorer", "Start Menu & Search", "Appearance", "System"),
            "minBuild": f.get("MinVersion"),
            "maxBuild": f.get("MaxVersion"),
            "profiles": profiles,
                        "apply": read_reg(apply_path),
            "undo": read_reg(undo_path),
        })
    return out


# Máximo rendimiento: lo del perfil Gamer + esto. Solo ajustes sin riesgo de conflicto con apps o juegos.
PROFILE_PERF = {"kiza.powerUltimate", "kiza.usbSuspend", "kiza.pcieLspm", "kiza.startupDelay"}

# Menos input delay: solo ajustes de entrada y energía que no cambian cómo funcionan apps ni juegos.
PROFILE_INPUT = {
    "q.DisableMouseAcceleration", "kiza.keyboardRepeat", "q.DisableStickyKeys", "kiza.accessibilityShortcuts",
    "kiza.usbSuspend", "kiza.pcieLspm", "kiza.powerUltimate", "q.DisableDVR", "kiza.gameMode",
}

# Retirados: pueden hacer fallar apps o juegos en algunos equipos (Photoshop, FiveM/GTA, anticheats, audio).
# Siguen en el catálogo solo para poder deshacerlos en quien ya los aplicó; la app no los muestra.
RETIRED = {
    "kiza.hags", "kiza.windowedOpt", "kiza.vrr", "kiza.mmcssGames", "kiza.networkThrottling",
    "kiza.powerThrottling", "kiza.disableMpo", "kiza.fseGlobal", "kiza.coreParking", "kiza.win32Priority",
    "kiza.timerResolution", "kiza.vmp", "kiza.memoryIntegrity", "kiza.bgApps", "q.DisableGameBarIntegration",
}

# --------------------------------------------------------------------------
# Tweaks propios de KizaTweaks
# --------------------------------------------------------------------------

def reg(hive, path, name, typ, value):
    return {"op": "reg.set", "hive": hive, "path": path, "name": name, "type": typ, "value": value}


def regdel(hive, path, name):
    return {"op": "reg.deleteValue", "hive": hive, "path": path, "name": name}


MMCSS = r"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile"
GAMES_TASK = MMCSS + r"\Tasks\Games"
TELEMETRY_TASKS = [
    r"\Microsoft\Windows\Application Experience\Microsoft Compatibility Appraiser",
    r"\Microsoft\Windows\Application Experience\ProgramDataUpdater",
    r"\Microsoft\Windows\Customer Experience Improvement Program\Consolidator",
    r"\Microsoft\Windows\Customer Experience Improvement Program\UsbCeip",
    r"\Microsoft\Windows\Feedback\Siuf\DmClient",
    r"\Microsoft\Windows\Feedback\Siuf\DmClientOnScenarioDownload",
]

# GUIDs de powercfg (subgrupo, ajuste)
PWR_USB = "2a737441-1930-4402-8d77-b2bebba308a3"
PWR_USB_SUSPEND = "48e6b7a6-50f5-4782-a5d4-53bb8f07e226"
PWR_PCIE = "501a4d13-42af-4429-9fd1-a8218c268e20"
PWR_PCIE_LSPM = "ee12f906-d277-404b-b6da-e5fa1a576df5"
PWR_CPU = "54533251-82be-4824-96c1-47b60b740d00"
PWR_CPU_MINCORES = "0cc5b647-c1df-4637-891a-dec35c318583"

KIZA = [
    # ---------------- Juegos: efecto documentado ----------------
    {
        "id": "kiza.hags", "category": "gaming", "impact": "latency", "risk": "safe",
        "name": "Programación de GPU acelerada por hardware (HAGS)",
        "description": "La GPU organiza su propia cola de trabajo en lugar del CPU. Frames más estables y requisito para DLSS Frame Generation.",
        "note": "Requiere GPU compatible (NVIDIA GTX 10 o superior, AMD RX 5000 o superior) y driver actualizado.",
        "requiresReboot": True, "profiles": ["gamer", "privacy"],
        "apply": [reg("HKLM", r"SYSTEM\CurrentControlSet\Control\GraphicsDrivers", "HwSchMode", "DWord", 2)],
        "undo": [reg("HKLM", r"SYSTEM\CurrentControlSet\Control\GraphicsDrivers", "HwSchMode", "DWord", 1)],
    },
    {
        "id": "kiza.windowedOpt", "category": "gaming", "impact": "latency", "risk": "safe",
        "name": "Optimizaciones para juegos en ventana",
        "description": "Pasa los juegos DirectX 10/11 en ventana o sin bordes al modelo de presentación 'flip': menos latencia y habilita VRR (G-Sync/FreeSync).",
        "note": None, "requiresReboot": False, "profiles": ["gamer", "privacy"],
        "apply": [{"op": "dx.set", "key": "SwapEffectUpgradeEnable", "value": "1"}],
        "undo": [{"op": "dx.set", "key": "SwapEffectUpgradeEnable", "value": "0"}],
    },
    {
        "id": "kiza.vrr", "category": "gaming", "impact": "latency", "risk": "safe",
        "name": "Frecuencia de actualización variable (VRR)",
        "description": "Permite VRR en juegos que no lo soportan de forma nativa, en monitores G-Sync/FreeSync.",
        "note": "Solo tiene efecto si tu monitor soporta VRR.",
        "requiresReboot": False, "profiles": ["gamer", "privacy"],
        "apply": [{"op": "dx.set", "key": "VRROptimizeEnable", "value": "1"}],
        "undo": [{"op": "dx.set", "key": "VRROptimizeEnable", "value": "0"}],
    },
    {
        "id": "kiza.gameMode", "category": "gaming", "impact": "fps", "risk": "safe",
        "name": "Modo de juego",
        "description": "Windows prioriza el juego en primer plano y pausa la instalación de actualizaciones y drivers mientras juegas.",
        "note": None, "requiresReboot": False, "profiles": ["gamer", "privacy"],
        "apply": [reg("HKCU", r"Software\Microsoft\GameBar", "AutoGameModeEnabled", "DWord", 1),
                  reg("HKCU", r"Software\Microsoft\GameBar", "AllowAutoGameMode", "DWord", 1)],
        "undo": [regdel("HKCU", r"Software\Microsoft\GameBar", "AutoGameModeEnabled"),
                 regdel("HKCU", r"Software\Microsoft\GameBar", "AllowAutoGameMode")],
    },
    {
        "id": "kiza.powerHigh", "category": "gaming", "impact": "fps", "risk": "moderate",
        "name": "Plan de energía: Alto rendimiento",
        "description": "El CPU sube de frecuencia más rápido y no se 'duerme' entre frames. Ganancia pequeña en CPUs modernas; más notable en equipos antiguos.",
        "note": "Más consumo y temperatura. En portátil, solo conectado a la corriente.",
        "requiresReboot": False, "profiles": [],
        "apply": [{"op": "power.scheme", "scheme": "high"}],
        "undo": [{"op": "power.scheme", "scheme": "balanced"}],
    },
    {
        "id": "kiza.powerUltimate", "category": "gaming", "impact": "fps", "risk": "moderate",
        "name": "Plan de energía: Máximo rendimiento",
        "description": "Plan oculto de Windows que elimina las micro-latencias de ahorro de energía. Pensado para escritorios.",
        "note": "No recomendado en portátiles: gasta batería y calienta más.",
        "requiresReboot": False, "profiles": [],
        "apply": [{"op": "power.scheme", "scheme": "ultimate"}],
        "undo": [{"op": "power.scheme", "scheme": "balanced"}],
    },
    {
        "id": "kiza.hibernate", "category": "system", "impact": "storage", "risk": "safe",
        "name": "Desactivar hibernación",
        "description": "Borra hiberfil.sys (del tamaño de tu RAM) del disco del sistema. Útil si tu disco C: es pequeño. No afecta FPS.",
        "note": "En portátiles perderás la opción Hibernar. También desactiva Inicio rápido.",
        "requiresReboot": False, "profiles": [],
        "apply": [{"op": "cmd", "file": "powercfg.exe", "args": "/hibernate off"}],
        "undo": [{"op": "cmd", "file": "powercfg.exe", "args": "/hibernate on"}],
        "detect": [reg("HKLM", r"SYSTEM\CurrentControlSet\Control\Power", "HibernateEnabled", "DWord", 0)],
    },
    {
        "id": "kiza.widgets", "category": "taskbar", "impact": "fps", "risk": "safe",
        "name": "Desactivar Widgets",
        "description": "Quita el panel de noticias y clima de la barra de tareas y la pantalla de bloqueo; deja de cargar en segundo plano.",
        "note": None, "requiresReboot": False, "restartExplorer": True, "profiles": ["gamer", "privacy"],
        "apply": [reg("HKLM", r"SOFTWARE\Policies\Microsoft\Dsh", "AllowNewsAndInterests", "DWord", 0)],
        "undo": [regdel("HKLM", r"SOFTWARE\Policies\Microsoft\Dsh", "AllowNewsAndInterests")],
    },
    {
        "id": "kiza.bgApps", "category": "gaming", "impact": "fps", "risk": "moderate",
        "name": "Bloquear apps en segundo plano",
        "description": "Las apps de la Microsoft Store dejan de ejecutarse cuando no están abiertas.",
        "note": "Apps de Store como WhatsApp o Spotify (versión Store) dejarán de notificar cerradas.",
        "requiresReboot": False, "profiles": [],
        "apply": [reg("HKCU", r"Software\Microsoft\Windows\CurrentVersion\BackgroundAccessApplications", "GlobalUserDisabled", "DWord", 1)],
        "undo": [reg("HKCU", r"Software\Microsoft\Windows\CurrentVersion\BackgroundAccessApplications", "GlobalUserDisabled", "DWord", 0)],
    },
    {
        "id": "kiza.telemetryServices", "category": "privacy", "impact": "privacy", "risk": "safe",
        "name": "Desactivar servicio y tareas de telemetría",
        "description": "Desactiva el servicio DiagTrack (Experiencias del usuario conectado y telemetría) y las tareas programadas de recolección de datos.",
        "note": None, "requiresReboot": False, "profiles": ["gamer", "privacy"],
        "apply": [{"op": "service", "name": "DiagTrack", "startType": "Disabled"}]
                 + [{"op": "task", "path": t, "enabled": False} for t in TELEMETRY_TASKS]
                 + [{"op": "env", "name": "POWERSHELL_TELEMETRY_OPTOUT", "value": "1"}],
        "undo": [{"op": "service", "name": "DiagTrack", "startType": "Automatic"}]
                + [{"op": "task", "path": t, "enabled": True} for t in TELEMETRY_TASKS]
                + [{"op": "env", "name": "POWERSHELL_TELEMETRY_OPTOUT", "value": None}],
    },
    {
        "id": "kiza.consumerFeatures", "category": "privacy", "impact": "cleanup", "risk": "safe",
        "name": "Bloquear apps promocionadas automáticas",
        "description": "Evita que Windows instale juegos y apps patrocinadas (Candy Crush, etc.) por su cuenta.",
        "note": "Solo aplica por completo en Windows Pro/Enterprise/Education.",
        "requiresReboot": False, "profiles": ["gamer", "privacy"],
        "apply": [reg("HKLM", r"SOFTWARE\Policies\Microsoft\Windows\CloudContent", "DisableWindowsConsumerFeatures", "DWord", 1)],
        "undo": [regdel("HKLM", r"SOFTWARE\Policies\Microsoft\Windows\CloudContent", "DisableWindowsConsumerFeatures")],
    },
    # ---------------- Energía para juegos (plan activo) ----------------
    {
        "id": "kiza.usbSuspend", "category": "gaming", "impact": "latency", "risk": "safe",
        "name": "Sin ahorro de energía en USB",
        "description": "Windows deja de 'dormir' los puertos USB para ahorrar energía. Evita micro-cortes del mouse, teclado o audífonos USB en medio de la partida.",
        "note": "Se aplica al plan de energía activo. En portátil, gasta un poco más de batería.",
        "requiresReboot": False, "profiles": ["perf"],
        "apply": [{"op": "power.setting", "sub": PWR_USB, "setting": PWR_USB_SUSPEND, "value": 0}],
        "undo": [{"op": "power.setting", "sub": PWR_USB, "setting": PWR_USB_SUSPEND, "value": 1}],
    },
    {
        "id": "kiza.pcieLspm", "category": "gaming", "impact": "latency", "risk": "safe",
        "name": "Sin ahorro de energía en PCI Express",
        "description": "La GPU y el SSD NVMe no entran en estados de bajo consumo del bus PCIe. Quita pequeñas demoras al 'despertar' entre cargas.",
        "note": "Se aplica al plan de energía activo. Los planes Alto y Máximo rendimiento ya lo traen así.",
        "requiresReboot": False, "profiles": ["perf"],
        "apply": [{"op": "power.setting", "sub": PWR_PCIE, "setting": PWR_PCIE_LSPM, "value": 0}],
        "undo": [{"op": "power.setting", "sub": PWR_PCIE, "setting": PWR_PCIE_LSPM, "value": 1}],
    },
    # ---------------- Entrada (input delay) ----------------
    {
        "id": "kiza.keyboardRepeat", "category": "system", "impact": "latency", "risk": "safe",
        "name": "Teclado con respuesta inmediata",
        "description": "Al mantener una tecla, se repite sin la espera inicial de Windows y a la velocidad máxima. Se nota al moverte en menús, chats y al escribir.",
        "note": "Se aplica al cerrar sesión o reiniciar.",
        "requiresReboot": True, "profiles": [],
        "apply": [reg("HKCU", r"Control Panel\Keyboard", "KeyboardDelay", "String", "0"),
                  reg("HKCU", r"Control Panel\Keyboard", "KeyboardSpeed", "String", "31")],
        "undo": [reg("HKCU", r"Control Panel\Keyboard", "KeyboardDelay", "String", "1"),
                 reg("HKCU", r"Control Panel\Keyboard", "KeyboardSpeed", "String", "31")],
    },
    {
        "id": "kiza.accessibilityShortcuts", "category": "system", "impact": "latency", "risk": "safe",
        "name": "Sin atajos de Teclas filtro y Teclas de alternancia",
        "description": "Mantener Shift derecho 8 segundos o Bloq Num 5 segundos ya no activa funciones que retrasan o ignoran tus teclas en medio de la partida.",
        "note": None, "requiresReboot": False, "profiles": [],
        "apply": [reg("HKCU", r"Control Panel\Accessibility\Keyboard Response", "Flags", "String", "122"),
                  reg("HKCU", r"Control Panel\Accessibility\ToggleKeys", "Flags", "String", "58")],
        "undo": [reg("HKCU", r"Control Panel\Accessibility\Keyboard Response", "Flags", "String", "126"),
                 reg("HKCU", r"Control Panel\Accessibility\ToggleKeys", "Flags", "String", "62")],
    },
    # ---------------- Seguridad vs rendimiento ----------------
    {
        "id": "kiza.vmp", "category": "security", "impact": "fps", "risk": "moderate",
        "name": "Desactivar Plataforma de máquina virtual (VMP)",
        "description": "La plataforma de virtualización puede reducir el rendimiento en juegos en algunas configuraciones.",
        "note": "Rompe WSL2, Docker, emuladores de Android y Windows Sandbox. Mide antes y después.",
        "requiresReboot": True, "profiles": [],
        "apply": [{"op": "feature", "name": "VirtualMachinePlatform", "enabled": False}],
        "undo": [{"op": "feature", "name": "VirtualMachinePlatform", "enabled": True}],
    },
    {
        "id": "kiza.memoryIntegrity", "category": "security", "impact": "fps", "risk": "risky",
        "name": "Desactivar Integridad de memoria (HVCI)",
        "description": "Puede costar rendimiento en algunos equipos. Lo recomendable es apagarla solo para jugar y reactivarla después.",
        "note": "Pierdes protección contra drivers maliciosos en el núcleo de Windows. Algunos anti-cheat la exigen activa. Aplícala solo si el benchmark muestra una mejora clara.",
        "requiresReboot": True, "profiles": [],
        "apply": [reg("HKLM", r"SYSTEM\CurrentControlSet\Control\DeviceGuard\Scenarios\HypervisorEnforcedCodeIntegrity", "Enabled", "DWord", 0)],
        "undo": [reg("HKLM", r"SYSTEM\CurrentControlSet\Control\DeviceGuard\Scenarios\HypervisorEnforcedCodeIntegrity", "Enabled", "DWord", 1)],
    },
    # ---------------- Experimentales (populares en foros, evidencia débil) ----------------
    {
        "id": "kiza.mmcssGames", "category": "experimental", "impact": "fps", "risk": "moderate",
        "name": "Prioridad multimedia para juegos (MMCSS)",
        "description": "Sube la prioridad de CPU/GPU que el programador multimedia de Windows asigna a los juegos y reduce la reserva para tareas de fondo (SystemResponsiveness 10).",
        "note": "Muy popular en foros; efecto medido inconsistente. Usa el benchmark para confirmar.",
        "requiresReboot": True, "profiles": [],
        "apply": [reg("HKLM", MMCSS, "SystemResponsiveness", "DWord", 10),
                  reg("HKLM", GAMES_TASK, "GPU Priority", "DWord", 8),
                  reg("HKLM", GAMES_TASK, "Priority", "DWord", 6),
                  reg("HKLM", GAMES_TASK, "Scheduling Category", "String", "High"),
                  reg("HKLM", GAMES_TASK, "SFIO Priority", "String", "High")],
        "undo": [reg("HKLM", MMCSS, "SystemResponsiveness", "DWord", 20),
                 reg("HKLM", GAMES_TASK, "GPU Priority", "DWord", 8),
                 reg("HKLM", GAMES_TASK, "Priority", "DWord", 2),
                 reg("HKLM", GAMES_TASK, "Scheduling Category", "String", "Medium"),
                 reg("HKLM", GAMES_TASK, "SFIO Priority", "String", "Normal")],
    },
    {
        "id": "kiza.networkThrottling", "category": "experimental", "impact": "latency", "risk": "moderate",
        "name": "Quitar límite de red durante multimedia",
        "description": "Desactiva el límite que Windows pone al tráfico de red mientras reproduce audio/video (NetworkThrottlingIndex).",
        "note": "Popular en foros para ping; no hay evidencia sólida de mejora en juegos online.",
        "requiresReboot": True, "profiles": [],
        "apply": [reg("HKLM", MMCSS, "NetworkThrottlingIndex", "DWord", 0xFFFFFFFF)],
        "undo": [reg("HKLM", MMCSS, "NetworkThrottlingIndex", "DWord", 10)],
    },
    {
        "id": "kiza.powerThrottling", "category": "experimental", "impact": "fps", "risk": "moderate",
        "name": "Desactivar limitación de energía de procesos",
        "description": "Windows deja de bajar la prioridad de energía a procesos en segundo plano (Power Throttling).",
        "note": "En portátiles reduce la batería. Efecto en FPS normalmente nulo si el juego está en primer plano.",
        "requiresReboot": True, "profiles": [],
        "apply": [reg("HKLM", r"SYSTEM\CurrentControlSet\Control\Power\PowerThrottling", "PowerThrottlingOff", "DWord", 1)],
        "undo": [regdel("HKLM", r"SYSTEM\CurrentControlSet\Control\Power\PowerThrottling", "PowerThrottlingOff")],
    },
    {
        "id": "kiza.coreParking", "category": "experimental", "impact": "fps", "risk": "moderate",
        "name": "Todos los núcleos del CPU despiertos",
        "description": "Desactiva el 'estacionamiento' de núcleos: el CPU no apaga núcleos para ahorrar energía. Puede mejorar los 1 % low en juegos que usan muchos hilos.",
        "note": "No lo uses en Ryzen X3D de dos chiplets (7900X3D, 7950X3D, 9900X3D, 9950X3D): dependen de esto para mandar el juego a los núcleos con caché extra. Más consumo en portátil.",
        "requiresReboot": False, "profiles": ["pro"],
        "apply": [{"op": "power.setting", "sub": PWR_CPU, "setting": PWR_CPU_MINCORES, "value": 100}],
        "undo": [{"op": "power.setting", "sub": PWR_CPU, "setting": PWR_CPU_MINCORES, "value": 10}],
    },
    {
        "id": "kiza.win32Priority", "category": "experimental", "impact": "latency", "risk": "moderate",
        "name": "Más prioridad a la ventana activa",
        "description": "Cambia cómo Windows reparte el CPU: turnos cortos y más tiempo para el programa en primer plano (Win32PrioritySeparation 0x26). Puede mejorar la respuesta del juego.",
        "note": "Popular en la escena competitiva; el efecto varía según el juego y el CPU. Mide con el Benchmark.",
        "requiresReboot": False, "profiles": ["pro"],
        "apply": [reg("HKLM", r"SYSTEM\CurrentControlSet\Control\PriorityControl", "Win32PrioritySeparation", "DWord", 0x26)],
        "undo": [reg("HKLM", r"SYSTEM\CurrentControlSet\Control\PriorityControl", "Win32PrioritySeparation", "DWord", 2)],
    },
    {
        "id": "kiza.timerResolution", "category": "experimental", "impact": "latency", "risk": "moderate",
        "name": "Resolución de temporizador global",
        "description": "Desde Windows 11 cada programa recibe su propia precisión de temporizador. Esto vuelve al modo anterior: si el juego pide 0,5 ms, se respeta aunque esté minimizado o haya otras apps. Ayuda a la consistencia de frame time en algunos juegos.",
        "note": "Aumenta un poco el consumo en reposo. En portátil no conviene.",
        "requiresReboot": True, "profiles": ["pro"], "minBuild": 22000,
        "apply": [reg("HKLM", r"SYSTEM\CurrentControlSet\Control\Session Manager\kernel", "GlobalTimerResolutionRequests", "DWord", 1)],
        "undo": [regdel("HKLM", r"SYSTEM\CurrentControlSet\Control\Session Manager\kernel", "GlobalTimerResolutionRequests")],
    },
    {
        "id": "kiza.startupDelay", "category": "experimental", "impact": "stability", "risk": "safe",
        "name": "Quitar retraso de apps de inicio",
        "description": "Las apps de inicio arrancan sin la espera de ~10 segundos que Windows agrega tras iniciar sesión.",
        "note": "El escritorio puede sentirse más pesado justo al iniciar sesión.",
        "requiresReboot": False, "profiles": [],
        "apply": [reg("HKCU", r"Software\Microsoft\Windows\CurrentVersion\Explorer\Serialize", "StartupDelayInMSec", "DWord", 0)],
        "undo": [regdel("HKCU", r"Software\Microsoft\Windows\CurrentVersion\Explorer\Serialize", "StartupDelayInMSec")],
    },
    {
        "id": "kiza.disableMpo", "category": "experimental", "impact": "stability", "risk": "moderate",
        "name": "Desactivar Multiplane Overlay (MPO)",
        "description": "Solución conocida para parpadeos y pantallazos negros en el navegador o con varios monitores.",
        "note": "Aplícalo solo si tienes parpadeos. Puede subir un poco el uso de GPU en escritorio.",
        "requiresReboot": True, "profiles": [],
        "apply": [reg("HKLM", r"SOFTWARE\Microsoft\Windows\Dwm", "OverlayTestMode", "DWord", 5)],
        "undo": [regdel("HKLM", r"SOFTWARE\Microsoft\Windows\Dwm", "OverlayTestMode")],
    },
    {
        "id": "kiza.fseGlobal", "category": "experimental", "impact": "latency", "risk": "moderate",
        "name": "Desactivar optimizaciones de pantalla completa (global)",
        "description": "Fuerza pantalla completa exclusiva 'clásica' en todos los juegos.",
        "note": "En Windows 11 suele empeorar Alt+Tab y puede romper VRR/Auto HDR. Mejor desactivarlo solo por juego desde Propiedades del .exe.",
        "requiresReboot": True, "profiles": [],
        "apply": [reg("HKCU", r"System\GameConfigStore", "GameDVR_FSEBehaviorMode", "DWord", 2),
                  reg("HKCU", r"System\GameConfigStore", "GameDVR_HonorUserFSEBehaviorMode", "DWord", 1),
                  reg("HKCU", r"System\GameConfigStore", "GameDVR_FSEBehavior", "DWord", 2),
                  reg("HKCU", r"System\GameConfigStore", "GameDVR_DXGIHonorFSEWindowsCompatible", "DWord", 1)],
        "undo": [reg("HKCU", r"System\GameConfigStore", "GameDVR_FSEBehaviorMode", "DWord", 0),
                 reg("HKCU", r"System\GameConfigStore", "GameDVR_HonorUserFSEBehaviorMode", "DWord", 0),
                 regdel("HKCU", r"System\GameConfigStore", "GameDVR_FSEBehavior"),
                 reg("HKCU", r"System\GameConfigStore", "GameDVR_DXGIHonorFSEWindowsCompatible", "DWord", 0)],
    },
]

for k in KIZA:
    k.setdefault("restartExplorer", False)
    k.setdefault("minBuild", None)
    k.setdefault("maxBuild", None)

CATEGORIES = [
    {"id": "gaming", "name": "Juegos", "icon": ""},
    {"id": "privacy", "name": "Privacidad", "icon": ""},
    {"id": "ai", "name": "IA", "icon": ""},
    {"id": "system", "name": "Sistema", "icon": ""},
    {"id": "start", "name": "Inicio y búsqueda", "icon": ""},
    {"id": "taskbar", "name": "Barra de tareas", "icon": ""},
    {"id": "explorer", "name": "Explorador", "icon": ""},
    {"id": "appearance", "name": "Apariencia", "icon": ""},
    {"id": "multitasking", "name": "Multitarea", "icon": ""},
    {"id": "update", "name": "Windows Update", "icon": ""},
    {"id": "security", "name": "Seguridad vs. rendimiento", "icon": ""},
    {"id": "experimental", "name": "Experimental", "icon": ""},
    {"id": "other", "name": "Otros", "icon": ""},
]

# --------------------------------------------------------------------------
# Apps: descripciones en español
# --------------------------------------------------------------------------

APP_ES = {
    "Clipchamp.Clipchamp": "Editor de video de Microsoft",
    "Microsoft.3DBuilder": "Modelado 3D básico",
    "Microsoft.549981C3F5F10": "Asistente Cortana (descontinuado)",
    "Microsoft.BingFinance": "Noticias financieras de Bing (descontinuado)",
    "Microsoft.BingFoodAndDrink": "Recetas de Bing (descontinuado)",
    "Microsoft.BingHealthAndFitness": "Salud y ejercicio de Bing (descontinuado)",
    "Microsoft.BingNews": "App de noticias de Microsoft",
    "Microsoft.BingSports": "Deportes de Bing (descontinuado)",
    "Microsoft.BingTranslator": "Traductor de Bing",
    "Microsoft.BingTravel": "Viajes de Bing (descontinuado)",
    "Microsoft.BingWeather": "Clima de Bing",
    "XP9CXNGPPJ97XX": "Asistente de IA Copilot",
    "Microsoft.Windows.AIHub": "Centro de IA de Copilot+",
    "Microsoft.PCManager": "Microsoft PC Manager (limpiador preinstalado)",
    "Microsoft.Getstarted": "Tips e introducción a Windows",
    "Microsoft.Messaging": "Mensajería (descontinuado)",
    "Microsoft.Microsoft3DViewer": "Visor de modelos 3D",
    "Microsoft.MicrosoftJournal": "Notas a mano para lápiz",
    "Microsoft.MicrosoftOfficeHub": "Acceso a Office / Microsoft 365",
    "Microsoft.MicrosoftPowerBIForWindows": "Power BI (análisis de negocios)",
    "Microsoft.MicrosoftSolitaireCollection": "Juegos de solitario",
    "Microsoft.MicrosoftStickyNotes": "Notas rápidas",
    "Microsoft.MixedReality.Portal": "Portal de realidad mixta",
    "Microsoft.NetworkSpeedTest": "Test de velocidad de internet",
    "Microsoft.Office.OneNote": "OneNote (versión UWP)",
    "Microsoft.Office.Sway": "Presentaciones Sway",
    "Microsoft.OneConnect": "Planes móviles",
    "Microsoft.Print3D": "Impresión 3D",
    "Microsoft.PowerAutomateDesktop": "Automatización de escritorio",
    "Microsoft.SkypeApp": "Skype (descontinuado)",
    "Microsoft.Todos": "Microsoft To Do",
    "Microsoft.Windows.DevHome": "Dev Home (descontinuado)",
    "Microsoft.WindowsAlarms": "Alarmas y reloj",
    "Microsoft.WindowsFeedbackHub": "Centro de opiniones",
    "Microsoft.WindowsMaps": "Mapas",
    "Microsoft.WindowsSoundRecorder": "Grabadora de sonido",
    "Microsoft.XboxApp": "Xbox Console Companion antiguo (descontinuado)",
    "Microsoft.ZuneVideo": "Películas y TV",
    "MicrosoftCorporationII.MicrosoftFamily": "Protección infantil de Microsoft",
    "MicrosoftCorporationII.QuickAssist": "Asistencia rápida (soporte remoto)",
    "MicrosoftTeams": "Teams personal (antiguo)",
    "MSTeams": "Microsoft Teams (nuevo)",
    "Microsoft.BingSearch": "Búsqueda web de Bing integrada en Windows",
    "Microsoft.GamingApp": "App Xbox: necesaria para Game Pass y algunos juegos de PC",
    "Microsoft.GetHelp": "Obtener ayuda: la usan algunos solucionadores de problemas",
    "Microsoft.M365Companions": "Mini-apps de Microsoft 365 empresarial",
    "Microsoft.MSPaint": "Paint 3D",
    "Microsoft.OneDrive": "Nube OneDrive",
    "Microsoft.OutlookForWindows": "Nuevo Outlook",
    "Microsoft.Paint": "Paint",
    "Microsoft.People": "Contactos (descontinuado)",
    "Microsoft.RemoteDesktop": "Escritorio remoto",
    "Microsoft.ScreenSketch": "Recortes (capturas de pantalla)",
    "Microsoft.StartExperiencesApp": "Feed de Widgets",
    "Microsoft.Whiteboard": "Pizarra colaborativa",
    "Microsoft.Windows.Photos": "Fotos",
    "Microsoft.WindowsCalculator": "Calculadora",
    "Microsoft.WindowsCamera": "Cámara",
    "Microsoft.windowscommunicationsapps": "Correo y Calendario (descontinuado)",
    "Microsoft.WindowsNotepad": "Bloc de notas",
    "Microsoft.WindowsStore": "Microsoft Store. No se puede reinstalar fácilmente",
    "Microsoft.WindowsTerminal": "Terminal de Windows",
    "Microsoft.Xbox.TCUI": "Componente de Xbox que usan la Store y algunos juegos",
    "Microsoft.XboxGameOverlay": "Overlay de Game Bar. Quitarlo puede bajar el rendimiento en ciertos CPUs modernos",
    "Microsoft.XboxGamingOverlay": "Xbox Game Bar. Quitarlo puede bajar el rendimiento en ciertos CPUs modernos",
    "Microsoft.XboxIdentityProvider": "Inicio de sesión de Xbox, necesario para algunos juegos",
    "Microsoft.XboxSpeechToTextOverlay": "Accesibilidad de voz en juegos. No se puede reinstalar fácilmente",
    "Microsoft.YourPhone": "Vínculo móvil (conectar tu celular)",
    "Microsoft.ZuneMusic": "Reproductor multimedia",
    "MicrosoftWindows.CrossDevice": "Integración con el celular en Explorador y Cámara",
    "MicrosoftWindows.Client.WebExperience": "Paquete que entrega Widgets y otras funciones",
    "Microsoft.WidgetsPlatformRuntime": "Motor de Widgets",
    "LGElectronics.LGMonitorApp": "App de monitores LG",
}

# Apps que KizaTweaks marca distinto a Win11Debloat por ser una app para gamers
APP_OVERRIDES = {
    # Edge: nunca desde la app (WebView2 y launchers dependen de él)
    "Microsoft.Edge": {"hidden": True},
    # Game Bar: Win11Debloat advierte pérdida de rendimiento en algunos CPUs
    "Microsoft.XboxGameOverlay": {"recommendation": "unsafe"},
    "Microsoft.XboxGamingOverlay": {"recommendation": "unsafe"},
}


# Apps de terceros que Windows instala o fija solo tras formatear y que no están en la lista base
EXTRA_APPS = [
    (["AppleInc.iTunes"], "iTunes", "Reproductor y tienda de Apple. Windows la instala o la sugiere solo."),
    (["AppleInc.AppleMusicWin"], "Apple Music", "App de música de Apple."),
    (["AppleInc.AppleTVWin"], "Apple TV", "App de video de Apple."),
    (["AppleInc.AppleDevices"], "Dispositivos Apple", "Para respaldar iPhone o iPad. Quítala si no tienes uno."),
    (["AppleInc.iCloud"], "iCloud", "Nube de Apple."),
    (["5319275A.WhatsAppDesktop"], "WhatsApp", "WhatsApp de escritorio (versión de la Store)."),
    (["FACEBOOK.317180B0BB486"], "Messenger", "Messenger de Facebook."),
    (["Microsoft.Copilot"], "Copilot (app)", "App de Copilot que Windows instala sola."),
    (["Microsoft.Edge.GameAssist"], "Game Assist de Edge", "Navegador superpuesto en juegos."),
    (["ROBLOXCORPORATION.ROBLOX"], "Roblox", "Juego que a veces viene fijado en Inicio."),
]


def build_apps(w11d):
    apps = json.load(open(os.path.join(w11d, "Config", "Apps.json"), encoding="utf-8-sig"))["Apps"]
    out = []
    for a in apps:
        ids = a["AppId"] if isinstance(a["AppId"], list) else [a["AppId"]]
        key = ids[0]
        ov = APP_OVERRIDES.get(key, {})
        if ov.get("hidden"):
            continue
        out.append({
            "ids": ids,
            "name": a["FriendlyName"],
            "description": APP_ES.get(key, a["Description"]),
            "recommendation": ov.get("recommendation", a["Recommendation"]),
            "selectedByDefault": bool(a["SelectedByDefault"]) and ov.get("recommendation", a["Recommendation"]) == "safe",
            "method": a["RemovalMethod"],
        })
    known = {i for a in out for i in a["ids"]}
    for ids, name, desc in EXTRA_APPS:
        if ids[0] in known:
            continue
        out.append({"ids": ids, "name": name, "description": desc, "recommendation": "safe",
                    "selectedByDefault": False, "method": "Appx"})
    return out


def main():
    if len(sys.argv) < 2:
        print(__doc__)
        sys.exit(1)
    w11d = sys.argv[1]
    try:
        commit = subprocess.check_output(["git", "-C", w11d, "rev-parse", "--short", "HEAD"], text=True).strip()
    except Exception:
        commit = "desconocido"
    tweaks = w11d_tweaks(w11d) + KIZA
    for t in tweaks:
        if t["id"] in PROFILE_PERF and "perf" not in t["profiles"]:
            t["profiles"].append("perf")
        if t["id"] in PROFILE_INPUT and "input" not in t["profiles"]:
            t["profiles"].append("input")
        if t["id"] in RETIRED:
            t["retired"] = True
            t["profiles"] = []
        if t["id"] == "kiza.startupDelay":
            t["category"] = "system"
    missing = (PROFILE_PERF | PROFILE_INPUT | RETIRED) - {t["id"] for t in tweaks}
    assert not missing, f"ids inexistentes: {missing}"
    assert not ((PROFILE_PERF | PROFILE_INPUT) & RETIRED)
    ids = [t["id"] for t in tweaks]
    assert len(ids) == len(set(ids)), "ids duplicados"
    for t in tweaks:
        assert t["apply"] and t["undo"], t["id"]
    os.makedirs(OUT_DIR, exist_ok=True)
    with open(os.path.join(OUT_DIR, "tweaks.json"), "w", encoding="utf-8") as f:
        json.dump({"version": 1,
                   "categories": CATEGORIES, "tweaks": tweaks}, f, ensure_ascii=False, indent=1)
    apps = build_apps(w11d)
    with open(os.path.join(OUT_DIR, "apps.json"), "w", encoding="utf-8") as f:
        json.dump({"version": 1, "apps": apps}, f, ensure_ascii=False, indent=1)
    print(f"tweaks: {len(tweaks)} | apps: {len(apps)}")


if __name__ == "__main__":
    main()
