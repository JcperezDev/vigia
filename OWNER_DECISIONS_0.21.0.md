# Decisiones del propietario para 0.21.0 — preparadas, sin aplicar

Cada punto trae lo que hay, lo que recomiendo y la frase exacta para autorizarlo. Nada de esto está aplicado.

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

## 3. Reactivar UAC en tu PC (`EnableLUA = 0` ahora)
Con UAC desactivado, todo lo que corre con tu cuenta es administrador, agentes incluidos. Por eso cambiar los
permisos de `.vigia` no basta. Recomiendo reactivarlo. **No lo he tocado.**
- Aplicar (consola de administrador): `reg add HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System /v EnableLUA /t REG_DWORD /d 1 /f` y reiniciar cuando quieras.
- Volver atrás: el mismo comando con `/d 0` y reiniciar.
- Efecto: los programas que piden administrador mostrarán el aviso. Los juegos y los antitrampas siguen funcionando igual.
**Para autorizarlo:** «reactivo UAC yo» (lo haces tú) o «autorizo reactivar UAC» (lo dejo preparado y el reinicio lo haces tú).

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
a) «autorizo quitar-acceso-sandbox en .vigia» (reversible).
b) «rota la clave» (clave nueva en un equipo aparte; `preparar-relevo.js anadir` en 0.21.0, firmada con la actual) **o** «mueve la clave actual» al equipo de firma.
c) Decidir las entradas de `CodexSandboxUsers` en `AppData` y `Temp` (afectan a otras herramientas).
d) Firmar fuera con `firmar-fuera.ps1`, repetir instalación y migración en VigiaLab con el paquete firmado y publicar (push, merge y release): solo con tu orden.

## 7. Otras decisiones que siguen abiertas
- Servicio «Sensores de Vigía»: fuera de 0.21.0 hasta que haya revisión externa y tu aprobación (`SENSOR_SERVICE_EXTERNAL_REVIEW.md`).
- Validar la temperatura en tu Ryzen: `SENSOR_RYZEN_VALIDATION.md` (no instala nada; tarda unos 10 minutos).
- UAC con usuario estándar: solo falta que teclees la contraseña de administrador en el aviso, dentro de la sesión `estandar` de VigiaLab.
