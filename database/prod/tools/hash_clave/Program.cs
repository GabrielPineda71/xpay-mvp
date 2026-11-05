// Piloto PROD — corrección 2a. Uso (desde la raíz del repositorio):
//
//   dotnet run --project database/prod/tools/hash_clave -- <usuario>
//
// Pide la clave dos veces sin mostrarla y escribe en la salida estándar SOLO
// el hash BCrypt (coste por defecto de BCrypt.Net, el mismo que usa el
// backend). La clave nunca se escribe en disco, en la salida ni en logs. El
// hash va a la variable ADMIN_HASH de operacion/crear_primer_admin.sql.
//
// Política: la del backend (AuthService.ValidarPoliticaClave) con mínimo de
// 12 caracteres en vez de 8. El administrador debe cambiarla en el primer
// ingreso (requiere_cambio_clave = 1).

const int LongitudMinima = 12;
const int LongitudMaxima = 128;
const string Especiales  = "!@#$%^&*()-_=+";

if (args.Length != 1 || string.IsNullOrWhiteSpace(args[0]))
{
    Console.Error.WriteLine("Uso: hash_clave <usuario>");
    return 2;
}
var usuario = args[0].Trim().ToLowerInvariant();

var clave = Leer("Clave: ");
if (clave != Leer("Repite la clave: "))
{
    Console.Error.WriteLine("Las claves no coinciden.");
    return 1;
}

var error = ValidarPolitica(clave, usuario);
if (error is not null)
{
    Console.Error.WriteLine(error);
    return 1;
}

var hash = BCrypt.Net.BCrypt.HashPassword(clave);
if (!BCrypt.Net.BCrypt.Verify(clave, hash))
{
    Console.Error.WriteLine("Error interno verificando el hash.");
    return 1;
}
Console.Out.WriteLine(hash);
return 0;

static string? ValidarPolitica(string clave, string usuario)
{
    if (clave.Length < LongitudMinima || clave.Length > LongitudMaxima)
        return $"La clave debe tener entre {LongitudMinima} y {LongitudMaxima} caracteres.";
    if (!clave.Any(char.IsUpper)) return "La clave debe incluir al menos una mayúscula.";
    if (!clave.Any(char.IsLower)) return "La clave debe incluir al menos una minúscula.";
    if (!clave.Any(char.IsDigit)) return "La clave debe incluir al menos un dígito.";
    if (!clave.Any(c => Especiales.Contains(c))) return $"La clave debe incluir al menos un carácter especial ({Especiales}).";
    if (clave.Contains(usuario, StringComparison.OrdinalIgnoreCase)) return "La clave no puede contener el nombre de usuario.";
    return null;
}

// Sin eco en terminal; con entrada redirigida (pruebas locales) lee una línea.
static string Leer(string prompt)
{
    Console.Error.Write(prompt);
    if (Console.IsInputRedirected)
    {
        var linea = Console.In.ReadLine() ?? string.Empty;
        Console.Error.WriteLine();
        return linea;
    }

    var sb = new System.Text.StringBuilder();
    while (true)
    {
        var k = Console.ReadKey(intercept: true);
        if (k.Key == ConsoleKey.Enter) break;
        if (k.Key == ConsoleKey.Backspace) { if (sb.Length > 0) sb.Length--; continue; }
        if (!char.IsControl(k.KeyChar)) sb.Append(k.KeyChar);
    }
    Console.Error.WriteLine();
    return sb.ToString();
}
