using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Newtonsoft.Json;
using MySql.Data.MySqlClient;
using System.Text;

namespace servicio;

public class modifica_carrito_compra
{
    class Datos { public int id_usuario; public string token; public int id_articulo; public int incremento; }

    [Function("modifica_carrito_compra")]
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
                    if (datos.incremento > 0)
                    {
                         var cmdCheck = new MySqlCommand("SELECT cantidad FROM stock WHERE id_articulo=@idA FOR UPDATE", conexion, transaccion);
                         cmdCheck.Parameters.AddWithValue("@idA", datos.id_articulo);
                         int stockActual = Convert.ToInt32(cmdCheck.ExecuteScalar());
                         
                         if (stockActual < 1)
                         {
                             transaccion.Rollback();
                             return new BadRequestObjectResult("{\"mensaje\":\"No hay suficientes artículos en stock\"}");
                         }
                    }

                    // Modificar Stock
                    var cmdStock = new MySqlCommand("UPDATE stock SET cantidad = cantidad - @inc WHERE id_articulo=@idA", conexion, transaccion);
                    cmdStock.Parameters.AddWithValue("@inc", datos.incremento);
                    cmdStock.Parameters.AddWithValue("@idA", datos.id_articulo);
                    cmdStock.ExecuteNonQuery();

                    // Modificar Carrito
                    var cmdCart = new MySqlCommand("UPDATE carrito_compra SET cantidad = cantidad + @inc WHERE id_usuario=@idU AND id_articulo=@idA", conexion, transaccion);
                    cmdCart.Parameters.AddWithValue("@inc", datos.incremento);
                    cmdCart.Parameters.AddWithValue("@idU", datos.id_usuario);
                    cmdCart.Parameters.AddWithValue("@idA", datos.id_articulo);
                    cmdCart.ExecuteNonQuery();

                    transaccion.Commit();
                    return new OkObjectResult("{\"mensaje\":\"Cantidad modificada\"}");
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