// Vigía de mentira para las pruebas de Puente. --install-check devuelve 0, o 3 si al lado hay un "romper.txt"
// (así se prueba una versión que no arranca). No hace nada más.
if (args.Length > 0 && args[0] == "--install-check")
    return File.Exists(Path.Combine(AppContext.BaseDirectory, "romper.txt")) ? 3 : 0;
return 0;
