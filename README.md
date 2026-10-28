# Vigía

Optimizador y monitor para PCs con Windows 10/11, pensado para jugar. Gratis, sin anuncios, sin cuentas y de código abierto.

**Descarga:** https://vigiapc.vercel.app · [Última versión](https://github.com/JcperezDev/vigia-releases/releases/latest)

## Qué hace

- **Monitor:** CPU, GPU, memoria y discos con los mismos contadores del Administrador de tareas, más temperaturas y qué programa está gastando de más.
- **Modo simple:** unas preguntas (impresora, red, apps de Windows…) y Vigía hace el resto.
- **Ajustes, Juegos y Memoria:** ajustes de Windows explicados uno a uno, FPS reales antes y después, perfil para Fortnite y liberación de memoria automática.
- **Apps de Windows, Inicio de Windows, Almacenamiento:** quitar lo que sobra, apagar lo que arranca solo y limpiar archivos temporales.
- **Diagnóstico, Salud de discos, BIOS:** revisa el PC y explica qué conviene cambiar. La BIOS solo se lee; Vigía nunca escribe en ella.
- **ISO de Windows:** crea una ISO limpia a partir de la oficial (sin anuncios, cuenta local, sin TPM ni Secure Boot, sin recopilación de datos ni BitLocker automático) y la pasa a un USB.

## Seguridad

- **Copia antes de cada cambio.** La primera vez guarda cómo estaba el sistema; cada ajuste se puede deshacer y «Volver a como estaba» lo devuelve todo.
- **No envía nada.** Sin telemetría. Solo pregunta a GitHub si hay versión nueva (se puede apagar).
- **Actualizaciones firmadas.** El actualizador comprueba una firma ECDSA antes de instalar.
- **No hace cosas peligrosas:** no apaga Defender, UAC ni las protecciones de la CPU; no usa drivers de terceros; no toca antitrampas ni procesos de juegos (solo archivos de configuración, con el juego cerrado).

## Compilar

Necesitas el SDK de .NET 10 en Windows.

```
cd app/Vigia
dotnet build -c Release
bin/Release/net10.0-windows/win-x64/Vigia.exe
```

Vigía pide permisos de administrador porque cambia ajustes de Windows.

## Estructura

| Carpeta | Contenido |
|---|---|
| `app/Vigia` | La app (.NET 10, WinForms + WebView2; la interfaz está en `wwwroot`) |
| `app/Puente` | Instalador y actualizador |
| `landing` | La web (https://vigiapc.vercel.app) |
| `tools` | Publicación, traducciones, iconos, fondo y pruebas en máquina virtual |

## Apoyar

Vigía es gratis. Si te sirve: dale una estrella, compártela o [invita un café en Ko-fi](https://ko-fi.com/vigiapc). ¿Algo falla o se te ocurre una mejora? Abre un [issue](https://github.com/JcperezDev/vigia/issues).

## Licencia

[MIT](LICENSE). Componentes de terceros en [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
