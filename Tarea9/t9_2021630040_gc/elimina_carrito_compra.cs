using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Newtonsoft.Json;
using MySql.Data.MySqlClient;
using System.Text;

namespace servicio;

public class elimina_carrito_compra
{
    class Datos { public int id_usuario; public string token; }

    [Function("elimina_carrito_compra")]
    public async Task<IActionResult> Run([HttpTrigger(AuthorizationLevel.Anonymous, "post")] HttpRequest req)
    {
        try
        {
            string body = await new StreamReader(req.Body).ReadToEndAsync();
            Datos? datos = JsonConvert.DeserializeObject<Datos>(body);
            if (datos == null) throw new Exception("Faltan datos");

            // 1. Auth HTTP
            using (var client = new HttpClient())
            {
                string urlUsuarios = Environment.GetEnvironmentVariable("EndpointUsuarios") ?? "http://localhost:7071/api/verifica_acceso";
                var jsonAuth = JsonConvert.SerializeObject(new { id_usuario = datos.id_usuario, token = datos.token });
                var contentAuth = new StringContent(jsonAuth, Encoding.UTF8, "application/json");
                var res = await client.PostAsync(urlUsuarios, contentAuth);
                if (!res.IsSuccessStatusCode) throw new Exception("Acceso denegado");
            }

            // 2. Lógica BD
            string cs = $"Server={Environment.GetEnvironmentVariable("Server")};UserID={Environment.GetEnvironmentVariable("UserID")};Password={Environment.GetEnvironmentVariable("Password")};Database={Environment.GetEnvironmentVariable("Database")};SslMode=Preferred;";
            
            using (var conexion = new MySqlConnection(cs))
            {
                conexion.Open();
                var transaccion = conexion.BeginTransaction();
                try
                {
                    // Devolver artículos al stock
                    string queryStock = @"UPDATE stock s 
                                          JOIN carrito_compra c ON s.id_articulo = c.id_articulo 
                                          SET s.cantidad = s.cantidad + c.cantidad 
                                          WHERE c.id_usuario = @idUsr";
                    
                    var cmdStock = new MySqlCommand(queryStock, conexion, transaccion);
                    cmdStock.Parameters.AddWithValue("@idUsr", datos.id_usuario);
                    cmdStock.ExecuteNonQuery();

                    // Borrar todo el carrito
                    var cmdDel = new MySqlCommand("DELETE FROM carrito_compra WHERE id_usuario = @idUsr", conexion, transaccion);
                    cmdDel.Parameters.AddWithValue("@idUsr", datos.id_usuario);
                    cmdDel.ExecuteNonQuery();

                    transaccion.Commit();
                    return new OkObjectResult("{\"mensaje\":\"Carrito vaciado correctamente\"}");
                }
                catch (Exception) { transaccion.Rollback(); throw; }
            }
        }
        catch (Exception e)
        {
            return new BadRequestObjectResult(JsonConvert.SerializeObject(new { mensaje = e.Message }));
        }
    }
}