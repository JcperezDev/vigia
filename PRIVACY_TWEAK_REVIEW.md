# Revisión de ajustes de privacidad nuevos (Fase 5)

Fecha: 2026-10-29 · Rama `debloat-v2` · Sin push, merge ni release.

Leyenda: `IMPLEMENTED` (código escrito), `UNIT_TESTED` (prueba automática ejecutada y en verde),
`VM_TESTED`, `HARDWARE_TESTED`, `BLOCKED`, `REJECTED`.

## Resumen

| id (propuesta de Codex) | Estado | Clave / valor / dato | Build mín. (Microsoft) |
|---|---|---|---|
| `privacy-tailored-experiences-off` | IMPLEMENTED + UNIT_TESTED | HKCU `Software\Policies\Microsoft\Windows\CloudContent` `DisableTailoredExperiencesWithDiagnosticData` = 1 | 15063 |
| `privacy-online-speech-off` | IMPLEMENTED + UNIT_TESTED | HKLM `SOFTWARE\Policies\Microsoft\InputPersonalization` `AllowInputPersonalization` = 0 | 10240 |
| `privacy-clipboard-history-off` | IMPLEMENTED + UNIT_TESTED | HKLM `Software\Policies\Microsoft\Windows\System` `AllowClipboardHistory` = 0 | 17763 |
| `privacy-clipboard-sync-off` | IMPLEMENTED + UNIT_TESTED | HKLM `Software\Policies\Microsoft\Windows\System` `AllowCrossDeviceClipboard` = 0 | 17763 |
| `privacy-cloud-search-off` | IMPLEMENTED + UNIT_TESTED | HKLM `SOFTWARE\Policies\Microsoft\Windows\Windows Search` `AllowCloudSearch` = 0 | 16299 |
| `privacy-settings-sync-off` | IMPLEMENTED + UNIT_TESTED | HKLM `Software\Policies\Microsoft\Windows\SettingSync` `DisableSettingSync` = 2 y `DisableSettingSyncUserOverride` = 1 | 22000 (CSP) |
| `privacy-handwriting-sharing-off` | REJECTED (por ahora) | `TabletPC\PreventHandwritingDataSharing` | — |
| `privacy-handwriting-errors-off` | REJECTED (por ahora) | `HandwritingErrorReports\PreventHandwritingErrorReports` | — |

Ninguno es `VM_TESTED` ni `HARDWARE_TESTED`: nada se aplicó en un Windows real. Ver «Qué no está probado».

## Evidencia (Microsoft Learn, descargado y leído el 2026-10-29)

- **Manage connections from Windows OS components to Microsoft services**
  (`learn.microsoft.com/en-us/windows/privacy/manage-connections-from-windows-operating-system-components-to-microsoft-services`):
  trae escrito literalmente «Create a REG_DWORD registry setting named `DisableTailoredExperiencesWithDiagnosticData` in
  `HKEY_CURRENT_USER\SOFTWARE\Policies\Microsoft\Windows\CloudContent`…» y «…named `DisableSettingSync` in
  `HKEY_LOCAL_MACHINE\Software\Policies\Microsoft\Windows\SettingSync` with a value of 2», más `DisableSettingSyncUserOverride` = 1.
- **Policy CSP — Privacy** (`…/client-management/mdm/policy-csp-privacy`): `AllowInputPersonalization` y `AllowCrossDeviceClipboard`
  con «Allowed values: 0 Not allowed», «Group policy mapping» con *Registry Key Name* y *Registry Value Name*, ediciones
  Pro/Enterprise/Education/IoT y versión mínima (10240 y 17763).
- **Policy CSP — Experience** (`…/policy-csp-experience`): `AllowClipboardHistory`, mismo formato, 17763.
- **Policy CSP — Search** (`…/policy-csp-search`): `AllowCloudSearch`, «0 Not allowed», clave
  `SOFTWARE\Policies\Microsoft\Windows\Windows Search`, 16299. El nombre del valor sale en la tabla de mapeo y en la hoja oficial
  *Group Policy Settings Reference* de Windows 11 24H2 (download.microsoft.com).
- **Policy CSP — ADMX_SettingSync** (`…/policy-csp-admx-settingsync`): confirma la directiva en 22000+.

Las cuatro propuestas que el informe de Codex dejó con «(URL a verificar)» eran voz en línea, las dos de escritura a mano y la
sincronización de preferencias. Voz en línea y sincronización quedan confirmadas con las páginas de arriba; las dos de escritura a
mano no (ver abajo), así que se quedan fuera del catálogo.

**Inferencia que queda (riesgo bajo):** para voz, portapapeles y búsqueda, el dato 0 en el registro sale de «Allowed values 0 = Not
allowed» del CSP con su mapeo de directiva; Microsoft no escribe «pon 0 en el registro» con esas palabras. Para tailored y
sincronización el dato está escrito literalmente.

## Por qué se rechazan las dos de escritura a mano

No tienen página CSP ni salen en «Manage connections»; solo están en la hoja de directivas, y el dato «1 = activado» no está
escrito en ninguna página. Además son funciones antiguas de Tablet PC. Se reabren si Microsoft documenta clave, valor y dato.
El selftest comprueba que ningún `privacy-handwriting*` entre al catálogo.

## Cómo se comportan (código)

Archivos: `app/Vigia/tweaks.json`, `app/Vigia/Tweaks.cs`, `app/Vigia/wwwroot/app.js`, `tools/i18n-extract.js`,
`tools/i18n-add-privacy.js`, `app/Vigia/wwwroot/i18n/{en,pt,fr,de,strings}.json`.

1. **Desactivados por defecto y solo a mano.** Campo nuevo `ask` (lo que cambia para la persona). Un ajuste con `ask`:
   - no entra en «Aplicar todo» (`twEasy` en `app.js`);
   - no lo propone el modo simple (el selftest lo comprueba sobre el plan completo de `Simple.Build`);
   - enseña «Lo que cambia: …» en su tarjeta, y al pulsar Aplicar sale un diálogo con las consecuencias de cada uno de los
     elegidos; si se cancela no se aplica nada.
2. **Candados en el motor** (`Tweaks.BlockedReason`, así que valen igual vengan de la interfaz o de otro sitio):
   - `minBuild`: si el Windows es más viejo que lo documentado → bloqueado.
   - `editions: "pro"`: Microsoft solo los documenta en Pro, Enterprise y Education → en Home (`EditionID` que empieza por `Core`)
     se bloquean.
   - `policy: true`: si el PC está administrado por una organización (`Policies.Managed()`) → bloqueado; y si la directiva ya
     existe con otro valor y no la puso Vigía (no hay copia suya) → bloqueado, no se pisa.
3. **Copia, comprobación y vuelta atrás**: el mismo camino que el resto del catálogo (`Apply` guarda el valor anterior o su
   ausencia, comprueba que quedó y, si falla a mitad, deshace; `Revert` deja la clave como estaba, borrando los valores que antes
   no existían).
4. **Sin promesas de rendimiento**: los textos solo hablan de privacidad y de lo que se pierde.
5. **Cinco idiomas**: 24 textos nuevos (títulos, detalles, avisos, bloqueos) en en/pt/fr/de; el español es el original.

## Pruebas ejecutadas

| Prueba | Resultado |
|---|---|
| `Vigia.exe --selftest` (datos aislados en scratch) | **314 OK / 0 fallos** (antes 302; +12 de esta fase) |
| — detectar: sin la clave = «sin aplicar», se puede elegir y trae su aviso | OK |
| — aplicar y comprobar los dos valores | OK |
| — restaurar borra los valores que no existían | OK |
| — Windows más viejo que el documentado: no se aplica | OK |
| — Windows Home: no se aplica | OK |
| — PC administrado: no se toca | OK |
| — directiva ajena con otro valor: ni se pisa ni se añade nada; restaurar sin copia no la borra | OK |
| — directiva ya puesta por otro con el mismo valor: sale como aplicada | OK |
| — catálogo: 6 directivas con aviso, fuente de Microsoft, solo claves `\Policies\Microsoft\` | OK |
| — escritura a mano fuera; modo simple no propone ninguno con aviso | OK |
| `node tools/i18n-check.js` | 2930 claves, 0 errores (en/pt/fr/de) |
| Comprobación propia de los 24 textos nuevos | extraídos y traducidos en los 4 idiomas |
| `Vigia.exe --snap … tweaks-all@en` y `@de` (fuera de pantalla) | los 6 avisos salen traducidos («What changes» / «Was sich ändert») |

Las pruebas de aplicar/restaurar usan una clave de prueba en `HKCU\Software\Vigia\SelfTest` (borrada al terminar, comprobado con
`reg query`). Ningún ajuste de privacidad se aplicó en este PC.

**Dato real de solo lectura:** la captura leyó el estado real de este PC y marcó como ya aplicados voz en línea, portapapeles entre
dispositivos, búsqueda en la nube y sincronización. Se comprobó con `reg query`: esas directivas ya existían en el registro con
justo esos valores (0, 0, 0, 2/1), puestas antes de esta sesión. La detección coincide con el registro.

**Traducción del aviso (revisado otra vez, build actual):** la captura de Ajustes en inglés y alemán ya muestra «What changes: …» y
«Was sich ändert: …» en los seis (`--snap tweaks-all@en|de`, 0 apariciones de «Lo que cambia»). La captura vieja en español era
anterior a las traducciones de `9f83671`. `UNIT_TESTED` (interfaz real, sin Windows de prueba).

**Estado «Aplicado» frente al registro (2.ª comprobación, solo lectura):** `AllowInputPersonalization=0`, `AllowCrossDeviceClipboard=0`,
`AllowCloudSearch=0`, `DisableSettingSync=2` + `DisableSettingSyncUserOverride=1` existen → la tarjeta dice «Applied»;
`AllowClipboardHistory` y `DisableTailoredExperiencesWithDiagnosticData` no existen → «Not applied». Coincide uno a uno. El valor
de `DisableSettingSync` en `tweaks.json` (2 y UserOverride 1) es el mismo que documenta Microsoft: discrepancia cerrada.

## Qué no está probado

- Efecto real en Windows (que Win+V quede vacío, que la búsqueda deje de mostrar OneDrive, etc.): `VM_TESTED` pendiente.
- Windows 10, Home real, PC unido a dominio real: solo simulado en el selftest.
- El diálogo de confirmación de la interfaz: código escrito, no pasado por la prueba de interfaz con Chrome.
- Si algún componente necesita cerrar sesión para notarlo: no medido (`reboot: false` no promete efecto inmediato).

## Próximo paso

En la VM con snapshot: aplicar cada uno desde Ajustes, comprobar el efecto visible, restaurar, y repetir con una cuenta estándar y
con UAC activo (las de HKLM piden el ayudante).
