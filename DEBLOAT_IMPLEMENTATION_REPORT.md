# Debloat: informe de implementación

Rama `debloat-v2`, que sale de `desarrollo-auditoria` (`efac823`). Sin push, merge ni release.

En cada sección se separa lo **implementado** (código que existe y compila), lo **probado** (con pruebas que se ejecutaron) y lo **no verificado** (no se ha probado en un Windows real, porque haría falta desinstalar algo).

## 1. Resumen

El módulo antiguo era una lista fija de unos 40 nombres de paquete. Hacía `Get-AppxPackage` y `Remove-AppxPackage` por nombre y se protegía con un filtro de frameworks. Ahora es un sistema por capas:

1. Inventario de las apps de Windows (usuario actual, otros usuarios y aprovisionadas) y de los programas clásicos del registro.
2. Catálogo versionado dentro del ejecutable.
3. Clasificación conservadora en cinco categorías.
4. Plan con vista previa obligatoria y una huella (token) de lo que se confirmó.
5. Ejecución de una app a la vez, con manifiesto previo, comprobación posterior y parada ante errores graves.
6. Historial con resultados reales y una recuperación que solo se ofrece cuando existe de verdad.

Nada se ejecuta al marcar una casilla. Nada útil, protegido, OEM ni desconocido viene marcado.

## 2. Auditoría del módulo anterior (confirmada en el código, commit `efac823`)

| # | Pregunta | Situación antes |
|---|---|---|
| 1–3 | Enumeración, nombre y PFN | `Get-AppxPackage -Name` solo sobre una lista fija. El nombre visible salía de la lista. El PFN no se usaba. |
| 4 | Apps por usuario | Solo el usuario actual. |
| 5 | Aprovisionadas | No se detectaban. |
| 6 | Eliminación | `Remove-AppxPackage` por nombre, en el usuario actual. |
| 7–8 | Modo simple y `KeepApps` | Ya corregido en la auditoría anterior (`RemoveApps`: solo lo que el usuario deja marcado; lista vacía = nada). Copilot, Alarmas, Grabadora y Spotify ya no salían marcadas. |
| 9 | Dependencias | No había. Solo se filtraban `IsFramework` y `NonRemovable`. |
| 10 | Permisos | La app entera se ejecuta como administrador (`requireAdministrator` en `app.manifest`). Ver §9. |
| 11 | Errores de PowerShell | Una advertencia en stdout podía romper el JSON. `ParseInstalled` ya no lanzaba excepciones, pero un fallo de lectura se veía como «ninguna app». |
| 12 | Desinstalación fallida | Se informaba por app. No había parada ni historial. |
| 13 | Recuperación | Copias con `Add-AppxPackage -Register` sobre el paquete de otro usuario. Funciona solo si los archivos siguen en Windows. |
| 14 | Tras una actualización de Windows | Nada. Las aprovisionadas podían volver en usuarios nuevos sin que Vigía lo supiera. |

## 3. Arquitectura final

| Archivo | Responsabilidad |
|---|---|
| `AppInventory.cs` | Detección. `IAppHost` (interfaz que se puede sustituir en las pruebas) y `WindowsAppHost`. PowerShell con parámetros por variables de entorno (nunca concatenados), marca `<<VIGIA-JSON>>` para separar el JSON de las advertencias, stderr aparte, tiempo máximo y cancelación. Registro Uninstall HKCU/HKLM 64/32 bits. Procesos en ejecución por ruta de imagen (solo lectura). Detección de equipo administrado con `NetGetJoinInformation`, sin red. Parser `AppParse` tolerante (0, 1 o N elementos, nulos, tipos raros, IDs inválidos). |
| `AppCatalog.cs` + `apps.json` | Catálogo v2 (61 entradas: 35 opcionales, 25 útiles y 1 OEM) más el bloque `protect` (nombres y prefijos). `AppClassifier` decide categoría, origen, riesgo, modo mínimo y nivel de recuperación. |
| `AppPlan.cs` | `AppPlanner.Build` (validación, dependencias, procesos abiertos, usuarios afectados, consecuencias) y `Execute` (token, revalidación, una app a la vez, parada). Programas clásicos: `msiexec /x {código}` o Configuración de Windows. |
| `AppHistory.cs` | Manifiesto previo (`pending`), resultado real, instantánea del inventario, recuperación A–D y reaparecidas. |
| `Debloat.cs` | Fachada fina: caché del inventario, `View`, `Preview`, `Run`, `Cancel`, `Restore`, `Export`. Mantiene `List`/`Remove` para el modo simple y la ISO, y ahora pasan por el plan en modo seguro. Incluye la batería de pruebas. |
| `Program.cs` | Mensajes `appsScan`, `appsPreview`, `appsRun`, `appsCancel`, `appsRestore` y `appsExport` con argumentos validados. CLI de solo lectura: `--apps-scan` y `--apps-plan`. |

El frontend no puede mandar órdenes de PowerShell. Solo manda claves de paquete, que el backend acepta si existen en el inventario leído, y el modo (`safe|custom|advanced`).

## 4. Categorías y protección

- **Sistema protegido** (nunca seleccionable, en ningún modo):
  - frameworks (`IsFramework`), paquetes no removibles, firmas `System`, recursos y paquetes opcionales;
  - cualquier paquete del que dependa otra app instalada;
  - los nombres y prefijos de `protect`: Store, StorePurchaseApp, App Installer/WinGet, Seguridad de Windows, Xbox TCUI e Identity Provider, Gaming Services, `Microsoft.Advertising.Xaml`, Store Engagement, AAD Broker, Edge, WebView, VCLibs, UI.Xaml, .NET Native y .NET, Windows App Runtime, DirectX Runtime, paquetes de idioma, `Windows.*`, `MicrosoftWindows.Client.*` y extensiones de imagen y vídeo;
  - en programas clásicos: runtimes, antitrampas, drivers y el propio Vigía.
- **Útiles** (no se marcan nunca; desde el modo Personalizado): Fotos, Calculadora, Bloc de notas, Paint, Recortes, Grabadora, Reloj, Outlook, Teams, OneDrive, Asistencia rápida, Vincular móvil, Xbox, Copilot, Spotify, To Do, etc.
- **Opcionales:** solo las del catálogo con fuente (Microsoft Learn o inventario observado). En modo seguro solo se recomiendan las de riesgo bajo instaladas en tu usuario.
- **OEM:** por editor o prefijo de fabricante. Nunca recomendadas; solo en Avanzado.
- **Desconocidas:** se muestran con su editor y origen. No se recomiendan; solo en Avanzado.

IDs que el prompt pedía investigar y **no se añadieron** por no tener fuente oficial de Microsoft: Correo y Calendario antiguos, LinkedIn, Bing Translator, Network Speed Test, Whiteboard y Journal. Si aparecen en un equipo, salen como «desconocida» (no recomendada). Asistencia rápida sí está confirmada (`MicrosoftCorporationII.QuickAssist`, Microsoft Learn).

## 5. Métodos

**Detección:**
- `Get-AppxPackage` (usuario actual) y `Get-AppxPackage -AllUsers`, que necesita administrador; sin administrador se avisa, no se miente.
- `Get-AppxProvisionedPackage -Online`.
- Registro Uninstall: HKCU, HKLM y WOW6432Node.
- Un mismo paquete visto por varias vías se junta en una sola entrada y conserva de dónde viene (tu usuario, otros usuarios, aprovisionada).

**Desinstalación:**
- `appx-user`: `Remove-AppxPackage -Package <full>`, solo en tu usuario.
- `deprovision`: `Remove-AppxProvisionedPackage -Online`. Es aparte, solo en Avanzado, con su propia casilla y una segunda confirmación.
- Otros usuarios: no se tocan en ningún modo.
- Win32 (solo Avanzado): MSI con `msiexec /x {GUID}`, donde el asistente del instalador pide confirmar. El resto abre Configuración › Apps.
- `UninstallString` nunca se ejecuta.

**Recuperación:**
- **A:** `Add-AppxPackage -Register` con el manifiesto que Windows aún guarda. Antes se comprueba que el archivo existe.
- **B:** abre la ficha de Microsoft Store por PFN. La instalas tú; Vigía no descarga nada.
- **C:** instrucciones manuales (Store bloqueada por directiva, programas clásicos).
- **D:** no recuperable (app retirada).

El botón «Recuperar» solo aparece con A o B y si la app no ha vuelto ya. Los datos internos de la app no vuelven, y la interfaz lo dice.

## 6. Modo simple (`Simple.cs`)

Ya en la auditoría anterior manda `RemoveApps` (lo que queda marcado). Ahora `Debloat.Remove` construye un plan en modo seguro, así que aunque llegue una útil, un framework o una protegida en la lista, no entra. Solo se quita de tu usuario, nunca el aprovisionamiento. La lista vacía o un inventario ilegible no quitan nada. Las apps de Xbox nunca. `Simple.cs` no necesitó cambios en este turno.

## 7. Interfaz (`index.html`, `app.js`, `app.css`)

- Tres modos: Seguro (predeterminado), Personalizado y Avanzado. Avanzado pide confirmación.
- Resumen: detectadas, opcionales, protegidas, desconocidas, programas clásicos y seleccionadas.
- Nueve filtros con contador.
- Buscador local sobre el inventario ya cargado: no consulta a Windows.
- Ficha por app: editor, origen, estado, riesgo, método, recuperación, dependencias e identificadores. Las protegidas aparecen sin casilla activa.
- Barra inferior: seleccionadas, «Revisar selección», «Cancelar» y «Marcar las recomendadas».
- Vista previa obligatoria con 10 datos de resumen y detalle por app (qué se quita, qué se queda, consecuencias, recuperación, apps abiertas). «Quitar ahora» se bloquea hasta aceptar, y hay una segunda casilla si cambia el aprovisionamiento.
- Progreso por app, botón para cancelar, resumen final, historial y exportación a JSON y CSV en Documentos\Vigía (sin rutas, usuarios ni SID).
- Se dibujan 200 filas; el resto aparece con «Mostrar todas».
- Un fallo de lectura se muestra como aviso, nunca como lista vacía.

Traducciones: todos los textos nuevos en es/en/pt/fr/de (`tools/i18n-add-debloat.js`). Los nombres técnicos de paquete se conservan.

## 8. Reaparecidas tras actualizaciones y comparación

`AppHistory.Compare` guarda una instantánea (solo nombres de paquete y de programa) y en cada análisis dice qué se instaló o desapareció desde el anterior. La instantánea del inventario permite marcar las opcionales que vuelven a aparecer después de haberlas quitado. Solo se avisa, nunca se quitan solas. No se bloquea la Store ni Windows Update, y no se promete que Windows no las reinstale.

## 9. Permisos

*(Actualizado con la arquitectura de permisos nueva, ver `docs/PRIVILEGES.md`.)*

Vigía ya no corre como administrador (`asInvoker`). Apps funciona así:

- **Sin el ayudante abierto:** `appsScan` y `appsPreview` son operaciones «suaves» y se hacen como tu usuario: inventario
  de tu usuario y programas clásicos. Lo que solo se ve con administrador (otros usuarios, aprovisionadas de la máquina)
  se marca como no leído, no como «no existe».
- **Con el ayudante (UAC una vez por sesión):** inventario completo, `appsRun`, `appsRestore`, `bloatRemove` y `simpleRun`.
  El ayudante vuelve a validar las claves (`IdRx`) y el token del plan; las claves nunca llegan a PowerShell.
- Quitar para tu usuario y quitar el aprovisionamiento siguen siendo autorizaciones separadas.
- El historial y el `pending` se escriben en `%LOCALAPPDATA%\Vigia` de la cuenta del escritorio (el ayudante ignora `VIGIA_DATA`).

**Sin verificar en VM con UAC activado:** que el inventario sin ayudante y con ayudante coincidan en lo que sí se puede leer,
y que una cuenta estándar vea su inventario sin errores.

## 10. Pruebas ejecutadas

| Prueba | Resultado |
|---|---|
| Compilación `dotnet build -c Release -r win-x64` (b33) | exit 0, 0 errores, 0 warnings CS |
| `Vigia.exe --selftest` (b33, `VIGIA_DATA` aislado) | **230 OK / 0 FAIL**; 73 son de apps, con un Windows falso (`FakeHost`) e historial en carpeta temporal |
| PowerShell real dentro del selftest | Unicode, parámetro hostil tratado como texto, error aparte, tiempo máximo y cancelación |
| `tools/ui-apps-test.js` (Chrome sin ventana, WebView falso, plan real de `--apps-plan`) | **44 OK / 0 FAIL**; incluye 700 apps dibujadas en 13 ms y la interfaz en inglés sin restos en español |
| `node tools/i18n-check.js` | 0 errores en los cinco idiomas |
| `node --check app.js` | OK |
| `--apps-scan` real en este PC (solo lectura) | 71 apps de Windows (63 protegidas, 6 útiles, 1 OEM, 1 desconocida; 10 aprovisionadas) y 58 programas clásicos (32 protegidos, 26 desconocidos). 0 recomendadas: este Windows ya estaba limpio |

Cubierto por las pruebas automáticas:
- **Inventario:** vacío, uno, 400, duplicados, IDs inválidos o con inyección, campos ausentes, framework, sistema, protegida, dependencia y aprovisionada sin instalar.
- **JSON:** vacío, malformado, advertencias antes del JSON, error parcial de permisos.
- **Plan y ejecución:** permitida, protegida, útil en Seguro, app abierta (no se cierra), fallo parcial, tres fallos seguidos (parada), error de permisos o directiva (parada inmediata), cancelación, plan distinto del confirmado, paquete desaparecido antes del «Sí».
- **Recuperación:** A, B, C (Store por directiva), D, ya reinstalada y PFN inválido.
- **Modo simple:** útiles y frameworks nunca, solo tu usuario, lista vacía e inventario ilegible.

## 11. Sin verificar (requiere VM con snapshot)

No se desinstaló nada real en el equipo de desarrollo. Pendiente en VM:

- `Remove-AppxPackage` real y la comprobación posterior.
- `Remove-AppxProvisionedPackage` y la creación de un usuario nuevo para confirmar que la app no aparece.
- Recuperación A con `-Register` tras quitar la app de un usuario mientras otro la conserva.
- Recuperación B (ficha de la Store).
- `msiexec /x` con un MSI de prueba.
- Detección de app abierta con una app real.
- Windows 10, 11 Home y Enterprise.
- Varios usuarios.
- Sin WinGet; el inventario no lo usa.
- Store deshabilitada por directiva.
- Equipo unido a dominio.
- Sin conexión.

## 12. Limitaciones y riesgos pendientes

- La UI se probó en Chrome con un WebView simulado, no dentro de WebView2 en Vigía.
- No se consultan IDs de WinGet para programas clásicos: WinGet puede no estar instalado y no se instala sin permiso.
- Sin iconos de app. Leerlos de forma segura implica tocar el manifiesto de cada paquete; se dejó fuera.
- No hay «perfil gaming» (cerrar apps temporalmente) ni recomendaciones por uso: no hay señales fiables y no se quiere prometer FPS.
- La elevación por operación está pendiente (§9).
- El catálogo depende de nombres de paquete. Si Microsoft renombra una app, saldrá como desconocida (conservador) hasta actualizar `apps.json`.

## 13. Archivos

Nuevos:
- `app/Vigia/AppInventory.cs`, `AppCatalog.cs`, `AppPlan.cs`, `AppHistory.cs`, `apps.json`
- `tools/i18n-add-debloat.js`, `tools/ui-apps-test.js`
- este informe

Modificados:
- `app/Vigia/Debloat.cs` (reescrito como fachada), `Program.cs`, `Vigia.csproj` (catálogo incrustado)
- `wwwroot/index.html`, `app.js`, `app.css`
- `wwwroot/i18n/strings.json`, `en.json`, `pt.json`, `fr.json`, `de.json`
- `tools/i18n-extract.js`

Sin cambios: `Simple.cs`, `Backups.cs`, `Security.cs`, `Settings.cs`. La copia antigua de apps en `Backups.cs` sigue igual y su aviso de límites ya era honesto.
