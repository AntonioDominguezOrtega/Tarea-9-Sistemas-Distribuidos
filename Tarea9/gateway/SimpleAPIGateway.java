/*
SimpleAPIGateway.java
Carlos Pineda G. 2024, 2025.

API Gateway sin conexión persistente con el cliente y con los hosts remotos.

Requiere las siguientes variables de entorno:

export keystore=keystore_servidor.jks
export password="1234567"

Se ejecuta así (-E permite al entorno de sudo utilizar las variables del entorno anterior):
sudo -E java SimpleAPIGateway
*/

import java.io.InputStream;
import java.io.OutputStream;
import java.io.IOException;
import java.io.InputStreamReader;
import java.io.PrintWriter;
import java.net.Socket;
import java.net.ServerSocket;
import javax.net.ssl.SSLServerSocketFactory;
import java.io.ByteArrayOutputStream;

class SimpleAPIGateway
{
  static String[][] tabla_enrutamiento =
  {
    // Microservicio Gestión de Usuarios (servicio-gu)
    {"/api/login", "servicio-gu", "80"},
    {"/api/alta_usuario", "servicio-gu", "80"},
    {"/api/consulta_usuario", "servicio-gu", "80"},
    {"/api/modifica_usuario", "servicio-gu", "80"},
    {"/api/borra_usuario", "servicio-gu", "80"},
    {"/api/verifica_acceso", "servicio-gu", "80"},

    // Microservicio Gestión de Artículos (servicio-ga)
    {"/api/alta_articulo", "servicio-ga", "80"},
    {"/api/consulta_articulos", "servicio-ga", "80"},
    {"/api/detalles_articulo", "servicio-ga", "80"},

    // Microservicio Gestión de Compras (servicio-gc)
    {"/api/compra_articulo", "servicio-gc", "80"},
    {"/api/consulta_carrito", "servicio-gc", "80"},
    {"/api/elimina_articulo_carrito_compra", "servicio-gc", "80"},
    {"/api/elimina_carrito_compra", "servicio-gc", "80"},
    {"/api/modifica_carrito_compra", "servicio-gc", "80"},

    // Servidor Web (servicio-sw) - Maneja archivos y Get
    {"/api/Get", "servicio-sw", "80"},
    {"/", "servicio-sw", "80"} // Por si se accede a la raíz
  };

  static int TIMEOUT_READ = 1000;  // milisegundos
  static Object obj = new Object();

  static class Worker_1 extends Thread
  {
    Socket cliente_1,cliente_2;

    Worker_1(Socket cliente_1)
    {
        this.cliente_1 = cliente_1;
    }

    // implementa el metodo readLine() de la clase BufferedReader pero sin buffer
    String readLine(InputStream in) throws IOException
    {
        StringBuilder line = new StringBuilder();

        int ch;
        boolean gotCR = false;

        while ((ch = in.read()) != -1) {
            if (ch == '\r') {
                gotCR = true;
                continue;
            }

            if (ch == '\n') {
                break;
            }

            if (gotCR) {
                // CR sin LF: lo agregamos
                line.append('\r');
                gotCR = false;
            }

            line.append((char) ch);
        }

        if (ch == -1 && line.length() == 0) {
            return null; // stream cerrado
        }

        return line.toString();
    }

    public void run()
    {
        try
        {
          InputStream entrada_1 = cliente_1.getInputStream();
          StringBuilder peticion = new StringBuilder();
          String linea;

          // lee la primera línea (URL) de la petición
          linea = readLine(entrada_1);

          // no hay más datos o se cerró el stream de entrada
          if (linea == null)
            return;

          System.out.println(linea);
          peticion.append(linea).append("\r\n");

          String[] v = linea.split(" ");
          String metodo = v[0];
          String funcion = v[1].split("\\?")[0];
          int longitud = 0;

          // si el método no está soportado, cierra la conexión con el cliente
          if (!metodo.equals("GET") && 
              !metodo.equals("POST") &&
              !metodo.equals("PUT") &&
              !metodo.equals("DELETE"))
            return;

          // lee los encabezados
          while ((linea = readLine(entrada_1)) != null)
          {
            if (linea.toLowerCase().startsWith("content-length:"))
              longitud = Integer.parseInt(linea.split(":")[1].trim());

            if (linea.equals(""))
              break;

            peticion.append(linea).append("\r\n");
          }

          peticion.append("\r\n");

          int i = 0;
          for (; i < tabla_enrutamiento.length; i++)
            if (funcion.equals(tabla_enrutamiento[i][0]))
            {
              String host_remoto = tabla_enrutamiento[i][1];
              int puerto_remoto = Integer.parseInt(tabla_enrutamiento[i][2]);

              // se conecta al host remoto
              cliente_2 = new Socket(host_remoto,puerto_remoto);

              // thread que redirige el tráfico del host remoto al cliente
              new Worker_2(cliente_1, cliente_2).start();

              OutputStream salida_2 = cliente_2.getOutputStream();
              salida_2.write(peticion.toString().getBytes("ASCII"));
              salida_2.flush();

              // lee y reenvia el body, si es el caso
              while (longitud > 0)
              {
                byte[] buffer = new byte[4096];
                // lee hasta el tamaño del buffer
                int n = cliente_1.getInputStream().read(buffer);
                salida_2.write(buffer,0,n);
                salida_2.flush();
                longitud -= n;
              }

              // espera que se cierre el socket cliente_2

              synchronized (obj)
              {
                obj.wait();
              }

              break;
            }
        }
        catch (Exception e)
        {
            // Conexión cerrada o error HTTP
        }
        finally
        {
            try { cliente_1.close(); } catch (Exception ignored) {}
        }
    }
  }

  static class Worker_2 extends Thread
  {
    Socket cliente_1,cliente_2;
    Worker_2(Socket cliente_1,Socket cliente_2)
    {
      this.cliente_1 = cliente_1;
      this.cliente_2 = cliente_2;
    }
    public void run()
    {
      try
      {
        cliente_2.setSoTimeout(TIMEOUT_READ);

        InputStream entrada_2 = cliente_2.getInputStream();
        OutputStream salida_1 = cliente_1.getOutputStream();

        byte[] buffer = new byte[4096];
        int n;

        while((n = entrada_2.read(buffer)) != -1)
        {
          salida_1.write(buffer,0,n);
          salida_1.flush();
        } 
      }
      catch (IOException e)
      {
        // conexión cerrada
      }
      finally
      {
        try
        {
          cliente_2.close();

          synchronized(obj)
          {
            obj.notify(); // notifica que se cerró el socket cliente_2
          }
        }
        catch (IOException e2)
        {
          e2.printStackTrace();
        }
      }
    }
  }

  public static void main(String[] args) throws Exception
  {
    String keystore = System.getenv("keystore");
    String password = System.getenv("password");

    if (keystore == null || password == null)
    { 
      System.err.println("No se definieron las variables de entorno keystore y password");
      System.exit(1);
    }

    System.setProperty("javax.net.ssl.keyStore",keystore);
    System.setProperty("javax.net.ssl.keyStorePassword",password);
    SSLServerSocketFactory socket_factory = (SSLServerSocketFactory)SSLServerSocketFactory.getDefault();
    ServerSocket ss = socket_factory.createServerSocket(443);

    for(;;)
    {
      // espera una conexión del cliente
      Socket cliente_1 = ss.accept();

      // thread que recibe la petición y la redirige al host remoto
      new Worker_1(cliente_1).start();
    }
  }
}
