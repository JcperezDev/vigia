# Decisiones del propietario para 0.21.0 — preparadas, sin aplicar

Cada punto trae lo que hay, lo que recomiendo y la frase exacta para autorizarlo. Actualizado 2026-10-30 18:00. Solo está aplicado lo que dice «Hecho» (permisos de `.vigia`, §6a).

## 1. Texto de temperatura (cinco idiomas)
Se ve en el Monitor cuando no hay una lectura válida del procesador (sin permisos, sin PawnIO o con un valor fuera de
rango). Va sin unidad y sin valor inventado. Comprobado en VigiaLab (fila 36).

| Idioma | Texto |
|---|---|
| es | Temperatura de CPU no disponible. |
| en | CPU temperature not available. |
| pt | Temperatura da CPU indisponível. |
| fr | Température CPU indisponible. |
| de | CPU-Temperatur nicht verfügbar. |

Debajo aparece el motivo (por ejemplo, que falta PawnIO o que hace falta abrir Vigía con permisos). En las notas
de versión, el README y PRODUCT no se prometen grados.
**Para aprobarlo:** «apruebo el texto de temperatura».

## 2. Seis ajustes de privacidad
Están implementados y probados en VigiaLab (aplicar y revertir). Todos son directivas documentadas en Microsoft
Learn, con copia previa. **Ninguno entra en «Aplicar todo» ni en el modo simple**: se muestran uno a uno, con su
consecuencia, antes de aplicarlos.

| id | Qué cambia | Consecuencia |
|---|---|---|
| `privacy-tailored-experiences-off` | HKCU `CloudContent\DisableTailoredExperiencesWithDiagnosticData = 1` | Sin sugerencias personalizadas con datos de diagnóstico |
| `privacy-online-speech-off` | HKLM `InputPersonalization\AllowInputPersonalization = 0` | El dictado y la voz en línea dejan de funcionar |
| `privacy-clipboard-history-off` | HKLM `System\AllowClipboardHistory = 0` | Win+V deja de guardar el historial |
| `privacy-clipboard-sync-off` | HKLM `System\AllowCrossDeviceClipboard = 0` | El portapapeles no se comparte entre dispositivos |
| `privacy-cloud-search-off` | HKLM `Windows Search\AllowCloudSearch = 0` | La búsqueda no muestra resultados de OneDrive ni de Outlook |
| `privacy-settings-sync-off` | HKLM `SettingSync\DisableSettingSync = 2` + `DisableSettingSyncUserOverride = 1` | La configuración de Windows no se sincroniza con tu cuenta |

Detalle y fuentes: `PRIVACY_TWEAK_REVIEW.md`. **Para aprobarlo:** «apruebo los seis ajustes de privacidad tal como
están» (o di cuál quitas).

## 3. Reactivar UAC en tu PC — preparado y probado en VigiaLab, **no aplicado**
**Estado actual (leído hoy, solo lectura):** `EnableLUA=0`, `ConsentPromptBehaviorAdmin=0`, `PromptOnSecureDesktop=0`. No hay directiva de grupo que fije UAC (`registry.pol` local no existe), así que el cambio manda.

**Consecuencias:** hoy todo lo que corre con tu cuenta es administrador sin preguntar, agentes de programación incluidos; por eso quitar permisos de `.vigia` no basta. Con UAC activo, los programas que piden administrador mostrarán el aviso y lo que no lo pida corre como usuario normal. Con tus juegos y antitrampas no se ha probado.

**Compatibilidad con Vigía:**
- 0.21.0 está hecha para UAC activo: abre sin pedir nada (`asInvoker`) y pide permiso solo al hacer algo administrativo (VM filas 33–36, con UAC activo).
- Tu 0.20.17 actual arranca con una tarea programada con privilegios máximos; esas tareas no muestran aviso al iniciar sesión, así que debería seguir abriendo igual. No probado en tu PC.

**Reinicio:** sí, `EnableLUA` solo cuenta tras reiniciar. Hasta entonces todo sigue como ahora.

**Cómo (consola de PowerShell como administrador, desde la carpeta del repo):**
- Ver sin tocar: `powershell -ExecutionPolicy Bypass -File tools\host\uac.ps1`
- Aplicar: `powershell -ExecutionPolicy Bypass -File tools\host\uac.ps1 -Activar` → guarda `%USERPROFILE%\uac-antes-<fecha>.json` y deja los valores de fábrica (1 / 5 / 1). Reinicia cuando quieras.
- Comprobar tras reiniciar: volver a «Ver sin tocar» (debe decir `EnableLUA=1  ConsentPromptBehaviorAdmin=5  PromptOnSecureDesktop=1`) y abrir algo que pida administrador: debe salir el aviso.
- Volver atrás: `uac.ps1 -Deshacer "%USERPROFILE%\uac-antes-<fecha>.json"` y reiniciar (vuelve exactamente a 0 / 0 / 0).
- Si tras reiniciar no puedes abrir una consola de administrador: Modo seguro → consola de administrador → el mismo `-Deshacer`; o, desde el entorno de recuperación, `reg load` de `SOFTWARE` y poner `EnableLUA` a 0.
- Probado en VigiaLab con tus valores simulados: ver no cambia nada, activar deja 1/5/1 con copia, deshacer deja 0/0/0 (fila 39). El efecto tras reiniciar no se probó.

**Para autorizarlo:** «reactivo UAC yo» (lo ejecutas tú con los pasos de arriba) o «autorizo reactivar UAC» (lo ejecuto yo y el reinicio lo haces tú).

## 4. Hyper-V (`hypervisorlaunchtype = Auto`, autorizado el 29-10)
Hace falta para VigiaLab. Si lo quitas, la VM no arranca hasta que vuelvas a ponerlo. Dejarlo activo puede costar
algo de rendimiento en algunos juegos. **No lo he tocado.**
- Quitar: `bcdedit /set {current} hypervisorlaunchtype off` y reiniciar. Respaldo del BCD: `scratch/p7/bcd-respaldo.bcd`.
- Volver a ponerlo: `bcdedit /set {current} hypervisorlaunchtype auto` y reiniciar.
**Para decidirlo:** «deja Hyper-V» o «quita Hyper-V cuando termine el laboratorio».

## 5. Migración de tu instalación 0.20.17
Probada en VigiaLab (fila 35). Tus datos de `%LOCALAPPDATA%\Vigia` se conservan; la carpeta vieja de programa, la
tarea de inicio con privilegios máximos y los accesos directos pasan a `C:\Program Files\Vigia`. **Solo con el paquete
firmado con la clave de producción** (antes no se puede: Puente rechaza los paquetes sin firmar).
- Antes: copia de `%LOCALAPPDATA%\Vigia` (la hace Puente; además, una copia manual en otro disco que no sea E:).
- Pasos: cerrar los juegos → `Vigia-Setup.exe` (firmado) → aceptar UAC → abrir Vigía → Monitor.
- Volver atrás: `Puente --uninstall` y reinstalar 0.20.17 desde `vigia-releases`; tus datos quedan.
**Para autorizarlo:** «autorizo migrar mi 0.20.17 a 0.21.0», después de firmar.

## 6. Firma (P0) — pasos exactos en `SECURITY_KEY_REVIEW.md`
a) ~~«autorizo quitar-acceso-sandbox en .vigia»~~ **Hecho el 30-10 con tu autorización** (copia de permisos `%USERPROFILE%\vigia-acl-antes-20261030-173546.acl`; volver atrás: `quitar-acceso-sandbox.ps1 -Deshacer <esa copia>`). Esto **no** demuestra que la clave no se leyera antes.
b) Elegir: **rotar** (recomendado, porque no se puede descartar la exposición) o **mover** la actual al equipo de firma. Rotar: clave nueva creada en el equipo de firma (`Puente.exe --genkey` allí); aquí `node tools/signing/preparar-relevo.js anadir <pública nueva>` (sube Puente a 2.0.2), commit, candidato, firma **con la clave actual** en el equipo de firma, prueba en VigiaLab y publicación; en la versión siguiente `promover`. Probado de punta a punta con claves de laboratorio (VM fila 38). Verificación: `preparar-relevo.js comprobar` rc 0 y `--key-check` rc 0 en el equipo de firma. Vuelta atrás antes de publicar: `git revert` del commit de `anadir`.
c) Permisos peligrosos fuera de `.vigia` (no tocados; corrección limitada propuesta, con copia y vuelta atrás, en `SECURITY_KEY_REVIEW.md` «Permisos fuera de .vigia»): `.puente-signing` y `Puente-respaldos` (entrada explícita de `CodexSandboxUsers`), `%LOCALAPPDATA%\Vigia` (lectura heredada de `AppData`). `AppData` y `Temp` en conjunto **no** se tocan. Para autorizarlo: «autorizo quitar CodexSandboxUsers de .puente-signing, Puente-respaldos y LOCALAPPDATA\Vigia».
d) Firmar fuera con `firmar-fuera.ps1`, repetir instalación limpia y migración desde 0.20.17 en VigiaLab con el paquete firmado exacto y publicar (push, merge y release): solo con tu orden. Para autorizarlo: «firma 0.21.0 fuera», y después «publica 0.21.0».

## 7. Otras decisiones que siguen abiertas
- Servicio «Sensores de Vigía»: fuera de 0.21.0 hasta que haya revisión externa y tu aprobación (`SENSOR_SERVICE_EXTERNAL_REVIEW.md`).
- Validar la temperatura en tu Ryzen: `SENSOR_RYZEN_VALIDATION.md` (no instala nada; tarda unos 10 minutos).
- UAC con usuario estándar: solo falta que teclees la contraseña de administrador en el aviso, dentro de la sesión `estandar` de VigiaLab (no se guarda en ningún sitio). Pasos: `Start-VM VigiaLab` → conectar como `estandar` → abrir Vigía → «ISO de Windows» → aviso UAC con la contraseña de `vigia` → debe aparecer la ventana del ayudante y, al cerrar, no quedar ningún proceso elevado. Para autorizarlo: «hago la prueba de usuario estándar».
- Cuentas `CodexSandboxOffline` / `CodexSandboxOnline` (habilitadas; la Offline inició sesión por última vez el 29-10 13:37): desactivarlas cuando no uses Codex (`Disable-LocalUser CodexSandboxOffline, CodexSandboxOnline`; volver: `Enable-LocalUser …`). Rompe el sandbox de Codex mientras estén desactivadas.
