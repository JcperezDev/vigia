# Vigia

Optimizador y monitor para PCs con Windows 10/11, pensado para jugar. Gratis, sin anuncios y sin cuentas.

[Read in English](README.md).

**Descarga:** https://vigiapc.vercel.app · [Última versión](https://github.com/JcperezDev/vigia/releases/latest)

Este repositorio guarda los instaladores y las actualizaciones de Vigia, y aquí se avisan fallos o se proponen ideas (Issues). El código está en un repositorio privado.

## Qué hace

- **Monitor:** CPU, GPU, memoria y discos con los mismos contadores del Administrador de tareas, más la temperatura del procesador cuando hay una fuente compatible y una lectura válida (no todos los procesadores lo son), y qué programa está gastando de más.
- **Modo simple:** unas preguntas (impresora, red, apps de Windows…) y Vigia hace el resto.
- **Ajustes, Juegos y Memoria:** ajustes de Windows explicados uno a uno, FPS reales antes y después, perfil para Fortnite, resolución estirada con estimación de FPS y liberación de memoria automática.
- **Apps de Windows, Inicio de Windows, Almacenamiento:** quitar lo que sobra, apagar lo que arranca solo y limpiar archivos temporales y la Papelera.
- **Diagnóstico, Salud de discos, BIOS:** revisa el PC y explica qué conviene cambiar. La BIOS solo se lee; Vigia nunca escribe en ella.
- **ISO de Windows:** crea una ISO limpia a partir de la oficial (sin anuncios, cuenta local, sin TPM ni Secure Boot, sin recopilación de datos ni BitLocker automático) y la pasa a un USB.

## Seguridad

- **Copia antes de cada cambio.** La primera vez guarda cómo estaba el sistema; cada ajuste se puede deshacer y «Volver a como estaba» lo devuelve todo.
- **No envía nada.** Sin telemetría. Solo pregunta a GitHub si hay versión nueva (se puede apagar).
- **Actualizaciones firmadas.** El actualizador comprueba la firma antes de instalar.
- **No hace cosas peligrosas:** no apaga Defender, UAC ni las protecciones de la CPU; el único driver que puede poner es PawnIO (código abierto y firmado, incluido sin cambios y comprobado por SHA-256) para ver los grados del procesador, y solo si dices que sí; al desinstalar Vigia se quita solo si lo puso Vigia; no toca antitrampas ni procesos de juegos (solo archivos de configuración, con el juego cerrado).

## Apoyar

Vigia es gratis. Si te sirve: dale una estrella, compártela o [dona con PayPal](https://www.paypal.com/donate/?hosted_button_id=6N3GP8FABMQ9U). ¿Algo falla o se te ocurre una mejora? Abre un [issue](https://github.com/JcperezDev/vigia/issues).

## Licencia

[PolyForm Noncommercial 1.0.0](LICENSE). Puedes usar y compartir Vigia gratis; lo que no se permite es venderla, cobrar por ella ni meterla en un producto o servicio de pago.

Las versiones hasta la 0.20.0 salieron con licencia MIT y quien las tenga conserva ese permiso para esas versiones. Componentes de terceros en [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
