using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Newtonsoft.Json;
using MySql.Data.MySqlClient;
using System.Text;

namespace servicio;

public class compra_articulo
{
    class DatosCompra { public int id_articulo; public int cantidad; public int id_usuario; public string token; }

    [Function("compra_articulo")]
    public async Task<IActionResult> Run([HttpTrigger(AuthorizationLevel.Anonymous, "post")] HttpRequest req)
    {
        try
        {
            string body = await new StreamReader(req.Body).ReadToEndAsync();
            DatosCompra? datos = JsonConvert.DeserializeObject<DatosCompra>(body);
            if (datos == null || datos.cantidad <= 0) throw new Exception("Datos inválidos");

            // --- 1. VERIFICAR ACCESO (Vía HTTP a Usuarios) ---
            using (var client = new HttpClient())
            {
                string urlUsuarios = Environment.GetEnvironmentVariable("EndpointUsuarios") ?? "http://localhost:7071/api/verifica_acceso";
                var jsonAuth = JsonConvert.SerializeObject(new { id_usuario = datos.id_usuario, token = datos.token });
                var contentAuth = new StringContent(jsonAuth, Encoding.UTF8, "application/json");
                var respuestaAuth = await client.PostAsync(urlUsuarios, contentAuth);
                if (!respuestaAuth.IsSuccessStatusCode) throw new Exception("Acceso denegado");
            }

            // --- 2. TRANSACCIÓN DE COMPRA (BD Local de Compras) ---
            string cs = $"Server={Environment.GetEnvironmentVariable("Server")};UserID={Environment.GetEnvironmentVariable("UserID")};Password={Environment.GetEnvironmentVariable("Password")};Database={Environment.GetEnvironmentVariable("Database")};SslMode=Preferred;";

            using (var conexion = new MySqlConnection(cs))
            {
                conexion.Open();
                var transaccion = conexion.BeginTransaction();
                try
                {
                    // Verificar Stock (La tabla stock en GC solo tiene id_articulo y cantidad)
                    var cmdStock = new MySqlCommand("SELECT cantidad FROM stock WHERE id_articulo = @idArt FOR UPDATE", conexion, transaccion);
                    cmdStock.Parameters.AddWithValue("@idArt", datos.id_articulo);
                    object result = cmdStock.ExecuteScalar();
                    
                    if (result == null) throw new Exception("El artículo no existe en el inventario");
                    int stockActual = Convert.ToInt32(result);

                    if (datos.cantidad > stockActual)
                    {
                        transaccion.Rollback();
                        return new BadRequestObjectResult("{\"mensaje\":\"No hay suficientes artículos en stock\"}");
                    }

                    // Restar Stock
                    var cmdUpdate = new MySqlCommand("UPDATE stock SET cantidad = cantidad - @cant WHERE id_articulo = @idArt", conexion, transaccion);
                    cmdUpdate.Parameters.AddWithValue("@cant", datos.cantidad);
                    cmdUpdate.Parameters.AddWithValue("@idArt", datos.id_articulo);
                    cmdUpdate.ExecuteNonQuery();

                    // Agregar al Carrito
                    string queryCarrito = @"INSERT INTO carrito_compra (id_usuario, id_articulo, cantidad) 
                                            VALUES (@idUsr, @idArt, @cant) 
                                            ON DUPLICATE KEY UPDATE cantidad = cantidad + @cant";
                    var cmdCarrito = new MySqlCommand(queryCarrito, conexion, transaccion);
                    cmdCarrito.Parameters.AddWithValue("@idUsr", datos.id_usuario);
                    cmdCarrito.Parameters.AddWithValue("@idArt", datos.id_articulo);
                    cmdCarrito.Parameters.AddWithValue("@cant", datos.cantidad);
                    cmdCarrito.ExecuteNonQuery();

                    transaccion.Commit();
                    return new OkObjectResult("{\"mensaje\":\"Compra realizada correctamente\"}");
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