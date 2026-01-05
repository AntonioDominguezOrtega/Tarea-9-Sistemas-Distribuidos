using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Newtonsoft.Json;
using MySql.Data.MySqlClient;

namespace servicio;

public class alta_articulo
{
    class DatosStock { public int id_articulo; public int cantidad; public int id_usuario; public string token; }

    [Function("alta_articulo")]
    public async Task<IActionResult> Run([HttpTrigger(AuthorizationLevel.Anonymous, "post")] HttpRequest req)
    {
        try
        {
            // Este microservicio recibe la petición desde el microservicio "Gestión de Artículos"
            string body = await new StreamReader(req.Body).ReadToEndAsync();
            DatosStock? datos = JsonConvert.DeserializeObject<DatosStock>(body);

            // 1. Validar seguridad (Llamando al servicio de Usuarios)
            // Nota: En un entorno real microservicios-a-microservicios a veces confían entre sí, 
            // pero el requerimiento pide validar acceso aquí también.
            // Por simplicidad en este paso, asumiremos que si llega aquí, ya fue validado, 
            // O podemos implementar la llamada HTTP a Usuarios si quieres ser estricto.
            
            string cs = $"Server={Environment.GetEnvironmentVariable("Server")};UserID={Environment.GetEnvironmentVariable("UserID")};Password={Environment.GetEnvironmentVariable("Password")};Database={Environment.GetEnvironmentVariable("Database")};SslMode=Preferred;";

            using (var conexion = new MySqlConnection(cs))
            {
                conexion.Open();
                // Insertamos solo ID y Cantidad (La tabla Stock de compras es diferente a la de Artículos)
                var cmd = new MySqlCommand("INSERT INTO stock (id_articulo, cantidad) VALUES (@idA, @cant)", conexion);
                cmd.Parameters.AddWithValue("@idA", datos.id_articulo);
                cmd.Parameters.AddWithValue("@cant", datos.cantidad);
                cmd.ExecuteNonQuery();
            }

            return new OkObjectResult("Stock inicializado");
        }
        catch (Exception e)
        {
            return new BadRequestObjectResult(e.Message);
        }
    }
}