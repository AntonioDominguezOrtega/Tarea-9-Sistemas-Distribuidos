using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Newtonsoft.Json;
using MySql.Data.MySqlClient;
using System.Text;

namespace servicio;

public class elimina_articulo_carrito_compra
{
    class DatosElimina { public int id_usuario; public string token; public int id_articulo; }

    [Function("elimina_articulo_carrito_compra")]
    public async Task<IActionResult> Run([HttpTrigger(AuthorizationLevel.Anonymous, "post")] HttpRequest req)
    {
        try
        {
            string body = await new StreamReader(req.Body).ReadToEndAsync();
            DatosElimina? datos = JsonConvert.DeserializeObject<DatosElimina>(body);
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
                    // Obtener cantidad a devolver
                    var cmdGet = new MySqlCommand("SELECT cantidad FROM carrito_compra WHERE id_usuario=@idU AND id_articulo=@idA", conexion, transaccion);
                    cmdGet.Parameters.AddWithValue("@idU", datos.id_usuario);
                    cmdGet.Parameters.AddWithValue("@idA", datos.id_articulo);
                    object res = cmdGet.ExecuteScalar();
                    
                    if (res != null)
                    {
                        int cantidadDevolver = Convert.ToInt32(res);

                        // Devolver al Stock
                        var cmdStock = new MySqlCommand("UPDATE stock SET cantidad = cantidad + @cant WHERE id_articulo=@idA", conexion, transaccion);
                        cmdStock.Parameters.AddWithValue("@cant", cantidadDevolver);
                        cmdStock.Parameters.AddWithValue("@idA", datos.id_articulo);
                        cmdStock.ExecuteNonQuery();

                        // Borrar del Carrito
                        var cmdDel = new MySqlCommand("DELETE FROM carrito_compra WHERE id_usuario=@idU AND id_articulo=@idA", conexion, transaccion);
                        cmdDel.Parameters.AddWithValue("@idU", datos.id_usuario);
                        cmdDel.Parameters.AddWithValue("@idA", datos.id_articulo);
                        cmdDel.ExecuteNonQuery();

                        transaccion.Commit();
                        return new OkObjectResult("{\"mensaje\":\"Artículo eliminado del carrito\"}");
                    }
                    else
                    {
                        transaccion.Rollback();
                        return new BadRequestObjectResult("{\"mensaje\":\"El artículo no está en el carrito\"}");
                    }
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