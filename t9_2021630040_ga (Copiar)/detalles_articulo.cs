using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Newtonsoft.Json;
using MySql.Data.MySqlClient;

namespace servicio;

public class detalles_articulo
{
    class ArticuloDetalle
    {
        public int id_articulo;
        public string nombre;
        public double precio;
        public string foto; 
    }

    [Function("detalles_articulo")]
    public IActionResult Run([HttpTrigger(AuthorizationLevel.Anonymous, "get")] HttpRequest req)
    {
        try
        {
            string idStr = req.Query["id_articulo"];
            if (string.IsNullOrEmpty(idStr)) return new BadRequestResult();
            int idArticulo = int.Parse(idStr);

            string cs = $"Server={Environment.GetEnvironmentVariable("Server")};UserID={Environment.GetEnvironmentVariable("UserID")};Password={Environment.GetEnvironmentVariable("Password")};Database={Environment.GetEnvironmentVariable("Database")};SslMode=Preferred;";

            using (var conexion = new MySqlConnection(cs))
            {
                conexion.Open();
                // Buscamos nombre, precio y foto por ID
                string query = @"SELECT a.id_articulo, a.nombre, a.precio, b.foto 
                                 FROM stock a 
                                 LEFT JOIN fotos_articulos b ON a.id_articulo = b.id_articulo 
                                 WHERE a.id_articulo = @id";

                var cmd = new MySqlCommand(query, conexion);
                cmd.Parameters.AddWithValue("@id", idArticulo);

                using (var reader = cmd.ExecuteReader())
                {
                    if (reader.Read())
                    {
                        var art = new ArticuloDetalle();
                        art.id_articulo = reader.GetInt32(0);
                        art.nombre = reader.GetString(1);
                        art.precio = reader.GetDouble(2);
                        if (!reader.IsDBNull(3)) art.foto = Convert.ToBase64String((byte[])reader["foto"]);
                        else art.foto = "";
                        
                        return new OkObjectResult(JsonConvert.SerializeObject(art));
                    }
                    else
                    {
                        return new NotFoundResult();
                    }
                }
            }
        }
        catch (Exception e)
        {
            return new BadRequestObjectResult(e.Message);
        }
    }
}