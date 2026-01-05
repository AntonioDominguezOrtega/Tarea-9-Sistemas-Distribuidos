using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Newtonsoft.Json;
using MySql.Data.MySqlClient;
using System.Text;

namespace servicio;

public class consulta_articulos
{
    class ArticuloResultado
    {
        public int id_articulo;
        public string nombre;
        public string descripcion;
        public double precio;
        public string foto; 
    }

    [Function("consulta_articulos")]
    public async Task<IActionResult> Run([HttpTrigger(AuthorizationLevel.Anonymous, "post")] HttpRequest req)
    {
        try
        {
            string body = await new StreamReader(req.Body).ReadToEndAsync();
            dynamic? datos = JsonConvert.DeserializeObject(body);
            if (datos == null) throw new Exception("Faltan parámetros");
            
            string search = datos.search;
            int id_usuario = datos.id_usuario;
            string token = datos.token;

            // --- 1. VERIFICAR ACCESO (Vía HTTP a Usuarios) ---
            using (var client = new HttpClient())
            {
                string urlUsuarios = Environment.GetEnvironmentVariable("EndpointUsuarios") ?? "http://localhost:7071/api/verifica_acceso";
                var jsonAuth = JsonConvert.SerializeObject(new { id_usuario = id_usuario, token = token });
                var contentAuth = new StringContent(jsonAuth, Encoding.UTF8, "application/json");
                var respuestaAuth = await client.PostAsync(urlUsuarios, contentAuth);
                if (!respuestaAuth.IsSuccessStatusCode) throw new Exception("Acceso denegado");
            }

            // --- 2. BUSCAR ARTÍCULOS (BD Local) ---
            string cs = $"Server={Environment.GetEnvironmentVariable("Server")};UserID={Environment.GetEnvironmentVariable("UserID")};Password={Environment.GetEnvironmentVariable("Password")};Database={Environment.GetEnvironmentVariable("Database")};SslMode=Preferred;";

            using (var conexion = new MySqlConnection(cs))
            {
                conexion.Open();
                string query = @"SELECT a.id_articulo, a.nombre, a.descripcion, a.precio, b.foto 
                                 FROM stock a 
                                 LEFT JOIN fotos_articulos b ON a.id_articulo = b.id_articulo 
                                 WHERE a.nombre LIKE @patron OR a.descripcion LIKE @patron";

                var cmd = new MySqlCommand(query, conexion);
                cmd.Parameters.AddWithValue("@patron", "%" + search + "%");

                var reader = cmd.ExecuteReader();
                var lista = new List<ArticuloResultado>();

                while (reader.Read())
                {
                    var art = new ArticuloResultado();
                    art.id_articulo = reader.GetInt32(0);
                    art.nombre = reader.GetString(1);
                    art.descripcion = reader.GetString(2);
                    art.precio = reader.GetDouble(3);
                    if (!reader.IsDBNull(4)) art.foto = Convert.ToBase64String((byte[])reader["foto"]);
                    else art.foto = "";
                    lista.Add(art);
                }
                return new OkObjectResult(JsonConvert.SerializeObject(lista));
            }
        }
        catch (Exception e)
        {
            return new BadRequestObjectResult(JsonConvert.SerializeObject(new { mensaje = e.Message }));
        }
    }
}