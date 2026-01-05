using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Newtonsoft.Json;
using MySql.Data.MySqlClient;
using System.Text;

namespace servicio;

public class consulta_carrito
{
    class ItemCarrito
    {
        public int id_articulo;
        public string nombre; // Se llenará consultando al microservicio de Artículos
        public int cantidad;
        public double precio; // Se llenará consultando al microservicio de Artículos
        public string foto;   // Se llenará consultando al microservicio de Artículos
        public double costo;
    }

    // Clase auxiliar para leer la respuesta del microservicio GA
    class InfoArticuloGA { public string nombre; public double precio; public string foto; }

    [Function("consulta_carrito")]
    public async Task<IActionResult> Run([HttpTrigger(AuthorizationLevel.Anonymous, "post")] HttpRequest req)
    {
        try
        {
            string body = await new StreamReader(req.Body).ReadToEndAsync();
            dynamic? datos = JsonConvert.DeserializeObject(body);
            if (datos == null) throw new Exception("Faltan datos");

            // 1. Verificar Acceso (HTTP a Usuarios)
            using (var client = new HttpClient())
            {
                string urlUsuarios = Environment.GetEnvironmentVariable("EndpointUsuarios") ?? "http://localhost:7071/api/verifica_acceso";
                var jsonAuth = JsonConvert.SerializeObject(new { id_usuario = (int)datos.id_usuario, token = (string)datos.token });
                var contentAuth = new StringContent(jsonAuth, Encoding.UTF8, "application/json");
                var respuestaAuth = await client.PostAsync(urlUsuarios, contentAuth);
                if (!respuestaAuth.IsSuccessStatusCode) throw new Exception("Acceso denegado");
            }

            // 2. Obtener items del carrito (Solo IDs y Cantidades desde BD Local)
            string cs = $"Server={Environment.GetEnvironmentVariable("Server")};UserID={Environment.GetEnvironmentVariable("UserID")};Password={Environment.GetEnvironmentVariable("Password")};Database={Environment.GetEnvironmentVariable("Database")};SslMode=Preferred;";
            var lista = new List<ItemCarrito>();

            using (var conexion = new MySqlConnection(cs))
            {
                conexion.Open();
                // OJO: Aquí ya no hacemos JOIN con stock ni fotos, porque no existen en esta BD
                string query = "SELECT id_articulo, cantidad FROM carrito_compra WHERE id_usuario = @idUsr";
                var cmd = new MySqlCommand(query, conexion);
                cmd.Parameters.AddWithValue("@idUsr", datos.id_usuario);

                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        var item = new ItemCarrito();
                        item.id_articulo = reader.GetInt32(0);
                        item.cantidad = reader.GetInt32(1);
                        // Dejamos temporalmente vacíos los datos que no tenemos
                        item.nombre = "Cargando...";
                        item.precio = 0;
                        item.foto = ""; 
                        lista.Add(item);
                    }
                }
            }

            // 3. Rellenar detalles consultando al Microservicio de Artículos (GA)
            using (var client = new HttpClient())
            {
                string endpointGA = Environment.GetEnvironmentVariable("EndpointArticulos") ?? "http://localhost:7072/api/detalles_articulo";
                
                foreach (var item in lista)
                {
                    try 
                    {
                        // Hacemos GET a detalles_articulo?id_articulo=...
                        var resp = await client.GetAsync($"{endpointGA}?id_articulo={item.id_articulo}");
                        if (resp.IsSuccessStatusCode)
                        {
                            string jsonDetalle = await resp.Content.ReadAsStringAsync();
                            var detalle = JsonConvert.DeserializeObject<InfoArticuloGA>(jsonDetalle);
                            if (detalle != null)
                            {
                                item.nombre = detalle.nombre;
                                item.precio = detalle.precio;
                                item.foto = detalle.foto;
                                item.costo = item.cantidad * item.precio;
                            }
                        }
                    }
                    catch { /* Si falla la conexión con GA, el item se queda con datos por defecto */ }
                }
            }

            return new OkObjectResult(JsonConvert.SerializeObject(lista));
        }
        catch (Exception e)
        {
            return new BadRequestObjectResult(JsonConvert.SerializeObject(new { mensaje = e.Message }));
        }
    }
}