using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Newtonsoft.Json;
using MySql.Data.MySqlClient;

namespace servicio;

public class verifica_acceso
{
    class Datos { public int id_usuario; public string token; }

    [Function("verifica_acceso")]
    public async Task<IActionResult> Run([HttpTrigger(AuthorizationLevel.Anonymous, "post")] HttpRequest req)
    {
        try
        {
            string body = await new StreamReader(req.Body).ReadToEndAsync();
            Datos? datos = JsonConvert.DeserializeObject<Datos>(body);
            
            // Usamos la lógica existente en tu archivo login.cs (que está en esta misma carpeta)
            // Como ambos están en el namespace 'servicio', sí se pueden ver.
            string cs = $"Server={Environment.GetEnvironmentVariable("Server")};UserID={Environment.GetEnvironmentVariable("UserID")};Password={Environment.GetEnvironmentVariable("Password")};Database={Environment.GetEnvironmentVariable("Database")};SslMode=Preferred;";
            
            using (var conexion = new MySqlConnection(cs))
            {
                conexion.Open();
                if (login.verifica_acceso(conexion, datos.id_usuario, datos.token))
                {
                    return new OkObjectResult("true"); // Es válido
                }
                else
                {
                    return new UnauthorizedResult(); // 401 No autorizado
                }
            }
        }
        catch (Exception)
        {
            return new BadRequestResult();
        }
    }
}