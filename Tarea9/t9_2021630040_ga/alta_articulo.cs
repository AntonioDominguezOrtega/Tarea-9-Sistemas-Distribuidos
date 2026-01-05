using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Newtonsoft.Json;
using MySql.Data.MySqlClient;
using System.Text;

namespace servicio;

public class alta_articulo
{
    class Articulo
    {
        public string? nombre;
        public string? descripcion;
        public double precio;
        public int cantidad;
        public string? foto; 
        public int id_usuario;
        public string? token;
    }

    [Function("alta_articulo")]
    public async Task<IActionResult> Run([HttpTrigger(AuthorizationLevel.Anonymous, "post")] HttpRequest req)
    {
        try
        {
            string body = await new StreamReader(req.Body).ReadToEndAsync();
            Articulo? articulo = JsonConvert.DeserializeObject<Articulo>(body);

            if (articulo == null) throw new Exception("Faltan datos");

            // --- PASO 1: Verificar Acceso (HTTP a Microservicio Usuarios) ---
            using (var client = new HttpClient())
            {
                // URL del servicio de usuarios (se configurará en variables de entorno)
                string urlUsuarios = Environment.GetEnvironmentVariable("EndpointUsuarios") ?? "http://localhost:7071/api/verifica_acceso";
                
                var jsonAuth = JsonConvert.SerializeObject(new { id_usuario = articulo.id_usuario, token = articulo.token });
                var contentAuth = new StringContent(jsonAuth, Encoding.UTF8, "application/json");
                
                var respuestaAuth = await client.PostAsync(urlUsuarios, contentAuth);
                
                if (!respuestaAuth.IsSuccessStatusCode)
                    throw new Exception("Acceso denegado (Token inválido)");
            }

            // --- PASO 2: Guardar Datos del Artículo (BD Local) ---
            string cs = $"Server={Environment.GetEnvironmentVariable("Server")};UserID={Environment.GetEnvironmentVariable("UserID")};Password={Environment.GetEnvironmentVariable("Password")};Database={Environment.GetEnvironmentVariable("Database")};SslMode=Preferred;";
            long idArticuloGenerado = 0;

            using (var conexion = new MySqlConnection(cs))
            {
                conexion.Open();
                var transaccion = conexion.BeginTransaction();
                try 
                {
                    // Insertar en Stock (Solo datos descriptivos)
                    var cmd = new MySqlCommand("INSERT INTO stock (nombre, descripcion, precio) VALUES (@nom, @desc, @prec)", conexion, transaccion);
                    cmd.Parameters.AddWithValue("@nom", articulo.nombre);
                    cmd.Parameters.AddWithValue("@desc", articulo.descripcion);
                    cmd.Parameters.AddWithValue("@prec", articulo.precio);
                    cmd.ExecuteNonQuery();
                    idArticuloGenerado = cmd.LastInsertedId;

                    // Insertar Foto
                    var cmdFoto = new MySqlCommand("INSERT INTO fotos_articulos (foto, id_articulo) VALUES (@foto, @idA)", conexion, transaccion);
                    cmdFoto.Parameters.AddWithValue("@foto", Convert.FromBase64String(articulo.foto));
                    cmdFoto.Parameters.AddWithValue("@idA", idArticuloGenerado);
                    cmdFoto.ExecuteNonQuery();

                    transaccion.Commit();
                }
                catch (Exception) { transaccion.Rollback(); throw; }
            }

            // --- PASO 3: Guardar Cantidad (HTTP a Microservicio Compras) ---
            using (var client = new HttpClient())
            {
                // URL del servicio de compras
                string urlCompras = Environment.GetEnvironmentVariable("EndpointCompras") ?? "http://localhost:7073/api/alta_articulo";

                // Enviamos ID generado, Cantidad y credenciales (por si compras valida seguridad)
                var jsonStock = JsonConvert.SerializeObject(new { 
                    id_articulo = idArticuloGenerado, 
                    cantidad = articulo.cantidad,
                    id_usuario = articulo.id_usuario,
                    token = articulo.token
                });
                var contentStock = new StringContent(jsonStock, Encoding.UTF8, "application/json");

                var respuestaStock = await client.PostAsync(urlCompras, contentStock);
                
                if (!respuestaStock.IsSuccessStatusCode)
                    // Nota: Si falla aquí, tendríamos una inconsistencia (Artículo creado sin stock). 
                    // En sistemas reales se usa "Compensación" (Saga pattern), pero para este prototipo basta con lanzar error.
                    throw new Exception("Error al registrar el stock en el microservicio de Compras");
            }

            return new OkObjectResult("{\"mensaje\":\"Artículo guardado correctamente\"}");
        }
        catch (Exception e)
        {
            return new BadRequestObjectResult(JsonConvert.SerializeObject(new { mensaje = e.Message }));
        }
    }
}