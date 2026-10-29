# Product

<!-- impeccable:product-schema 1 -->

## Platform

web

(La app Vigía es de escritorio para Windows, con interfaz WebView2; la landing es web estática.)

## Stack

- App: .NET 10 WinForms + WebView2 (`app/Vigia`), instalador/actualizador Puente (`app/Puente`).
- Landing: HTML/CSS estático en `landing/`, publicado en Vercel (https://vigiapc.vercel.app).
- Sin cuentas ni servidor propio: los enlaces de donación se leen de `support.json` en la raíz de este repo.

## Users

Jugadores de PC con Windows 10/11 que quieren más FPS, menos latencia y un PC limpio sin romper nada. Los usuarios bajan la app desde un enlace, la abren antes de jugar y vuelven a ella cuando el PC se siente lento.

## Product Purpose

Optimizar el PC de juego desde una sola app: monitor de hardware, limpieza, liberación de RAM, ajustes de Windows, apps que arrancan con Windows, resolución estirada, herramientas y copias de seguridad. Éxito: el usuario nota el PC más ágil y puede deshacer cualquier cambio.

## Positioning

- Hace una copia antes de cada cambio; todo se puede deshacer desde la app.
- No consume recursos en segundo plano: minimizada baja de ~4,4 % a ~0,1 % de un núcleo y de ~456 a ~50 MB de RAM (medido con `--bench`).
- No usa drivers vulnerables, activadores ni escribe en la BIOS; el único driver es PawnIO, con permiso del usuario; no apaga Defender, UAC ni Windows Update.
- Los ajustes de riesgo medio o alto vienen apagados y marcados.

## Capabilities and Constraints

- Monitor (CPU, GPU, RAM, discos, temperaturas con permisos de administrador), Almacenamiento, Diagnóstico, Optimizar, Memoria (liberación automática al 60 %), Ajustes (35 en catálogo), Apps, Herramientas (incl. FanControl), Inicio de Windows, Resolución estirada con vuelta automática en 15 s, Copias de seguridad, Configuración.
- Idiomas: español, inglés, portugués, francés y alemán.
- Actualizaciones firmadas (ECDSA) desde la propia app, con vuelta a la versión anterior.
- Instalador: `Vigia-X.Y.Z-win-x64.zip` + `Puente.exe` en GitHub Releases de este repo (`JcperezDev/vigia`). `JcperezDev/vigia-releases` queda como copia para las instalaciones ≤ 0.20.17, que todavía buscan ahí.
- Gratis por completo. Sin planes de pago ni licencias. Sección «Apoya Vigía» con donaciones opcionales que no desbloquean nada (decisión del 2026-10-27).
- Recorrido guiado la primera vez (9 pasos, se repite desde Ayuda) y sección Ayuda con guía de secciones y preguntas.

## Brand Commitments

- Nombre: Vigía. Icono: `app/Vigia/vigia.ico`.
- Voz: español claro y directo, sin jerga de marketing; no debe parecer hecha con IA (nada de «en vivo», brillos, frases infladas).
- No copiar código, textos ni recursos de otras apps.

## Evidence on Hand

- Mediciones reales: consumo en reposo (arriba); liberación de RAM de prueba 11,2 GB (52 % → 16 %).
- Capturas reales de la app con `Vigia.exe --snap <png> <vista>[@idioma]`.
- No hay testimonios, número de usuarios, reseñas ni benchmarks de FPS publicables: no inventarlos.

## Product Principles

1. Nada se cambia sin copia y sin forma de deshacerlo.
2. Lo arriesgado se marca, viene apagado y se prueba en VM antes.
3. La app no gasta recursos cuando no se está usando.
4. Decir lo que hace cada ajuste en palabras simples.

## Accessibility & Inclusion

Textos legibles en pantallas de 1080p a 1440p; contraste suficiente sobre fondo oscuro; landing usable con teclado y en móvil.
