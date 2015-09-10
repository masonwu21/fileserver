using System;
using System.Collections;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Web;

// offered to the public domain for any use with no restriction
// and also with no warranty of any kind, please enjoy. - David Jeske. 

// simple HTTP explanation
// http://www.jmarshall.com/easy/http/

namespace Bend.Util {

    public class HttpProcessor {
        public TcpClient socket;        
        public HttpServer srv;

        private Stream inputStream;
        public StreamWriter outputStream;

        public String http_method;
        public String http_url;
        public String http_protocol_versionstring;
        public Hashtable httpHeaders = new Hashtable();


        private static int MAX_POST_SIZE = 10 * 1024 * 1024; // 10MB

        public HttpProcessor(TcpClient s, HttpServer srv) {
            this.socket = s;
            this.srv = srv;                   
        }
        

        private string streamReadLine(Stream inputStream) {
            int next_char;
            string data = "";
            while (true) {
                next_char = inputStream.ReadByte();
                if (next_char == '\n') { break; }
                if (next_char == '\r') { continue; }
                if (next_char == -1) { Thread.Sleep(1); continue; };
                data += Convert.ToChar(next_char);
            }            
            return data;
        }
        public void process() {                        
            // we can't use a StreamReader for input, because it buffers up extra data on us inside it's
            // "processed" view of the world, and we want the data raw after the headers
            inputStream = new BufferedStream(socket.GetStream());

            // we probably shouldn't be using a streamwriter for all output from handlers either
            outputStream = new StreamWriter(new BufferedStream(socket.GetStream()));
            try
            {
                parseRequest();
                readHeaders();
                if (http_method.Equals("GET"))
                {
                    handleGETRequest();
                }
                else if (http_method.Equals("POST"))
                {
                    handlePOSTRequest();
                }

                outputStream.Flush();
            }
            catch (Exception e)
            {
                Console.WriteLine("Exception: " + e.ToString());
                writeFailure();
            }
            finally {

                // bs.Flush(); // flush any remaining output
                inputStream = null; outputStream = null; // bs = null;            
                socket.Close();
            }
            
        }

        public void parseRequest() {
            String request = streamReadLine(inputStream);
            string[] tokens = request.Split(' ');
            if (tokens.Length != 3) {
                throw new Exception("invalid http request line");
            }
            http_method = tokens[0].ToUpper();
            http_url = tokens[1];
            http_protocol_versionstring = tokens[2];

            Console.WriteLine("starting: " + request);
        }

        public void readHeaders() {
            Console.WriteLine("readHeaders()");
            String line;
            while ((line = streamReadLine(inputStream)) != null) {
                if (line.Equals("")) {
                    Console.WriteLine("got headers");
                    return;
                }
                
                int separator = line.IndexOf(':');
                if (separator == -1) {
                    throw new Exception("invalid http header line: " + line);
                }
                String name = line.Substring(0, separator);
                int pos = separator + 1;
                while ((pos < line.Length) && (line[pos] == ' ')) {
                    pos++; // strip any spaces
                }
                    
                string value = line.Substring(pos, line.Length - pos);
                Console.WriteLine("header: {0}:{1}",name,value);
                httpHeaders[name] = value;
            }
        }

        public void handleGETRequest() {
            srv.handleGETRequest(this);
        }

        private const int BUF_SIZE = 4096;
        public void handlePOSTRequest() {
            // this post data processing just reads everything into a memory stream.
            // this is fine for smallish things, but for large stuff we should really
            // hand an input stream to the request processor. However, the input stream 
            // we hand him needs to let him see the "end of the stream" at this content 
            // length, because otherwise he won't know when he's seen it all! 

            Console.WriteLine("get post data start");
            int content_len = 0;
            MemoryStream ms = new MemoryStream();
            if (this.httpHeaders.ContainsKey("Content-Length")) {
                 content_len = Convert.ToInt32(this.httpHeaders["Content-Length"]);
                 if (content_len > MAX_POST_SIZE) {
                     throw new Exception(
                         String.Format("POST Content-Length({0}) too big for this simple server",
                           content_len));
                 }
                 byte[] buf = new byte[BUF_SIZE];              
                 int to_read = content_len;
                 while (to_read > 0) {  
                     Console.WriteLine("starting Read, to_read={0}",to_read);

                     int numread = this.inputStream.Read(buf, 0, Math.Min(BUF_SIZE, to_read));
                     Console.WriteLine("read finished, numread={0}", numread);
                     if (numread == 0) {
                         if (to_read == 0) {
                             break;
                         } else {
                             throw new Exception("client disconnected during post");
                         }
                     }
                     to_read -= numread;
                     ms.Write(buf, 0, numread);
                 }
                 ms.Seek(0, SeekOrigin.Begin);
            }
            Console.WriteLine("get post data end");
            srv.handlePOSTRequest(this, new StreamReader(ms));

        }

        public void writeSuccess(string content_type="text/html" ,long length=0) {
            // this is the successful HTTP response line
            outputStream.WriteLine("HTTP/1.0 200 OK");  
            // these are the HTTP headers...          
            outputStream.WriteLine("Content-Type: " + content_type);
            if (length!=0)
            {
                //outputStream.WriteLine("Accept-Ranges: bytes");
                outputStream.WriteLine("Content-Length: " + length);
            }
            outputStream.WriteLine("Connection: close");
            // ..add your own headers here if you like

            outputStream.WriteLine(""); // this terminates the HTTP headers.. everything after this is HTTP body..
        }

        public void writeFailure() {
            // this is an http 404 failure response
            outputStream.WriteLine("HTTP/1.0 404 File not found");
            // these are the HTTP headers
            outputStream.WriteLine("Connection: close");
            // ..add your own headers here

            outputStream.WriteLine(""); // this terminates the HTTP headers.
        }
    }

    public abstract class HttpServer {

        protected int port;
        TcpListener listener;
        bool is_active = true;
       
        public HttpServer(int port) {
            this.port = port;
        }


        public void listen() {
            listener = new TcpListener(IPAddress.Loopback,port);
            listener.Start();
            while (is_active) {                
                TcpClient s = listener.AcceptTcpClient();
                HttpProcessor processor = new HttpProcessor(s, this);
                Thread thread = new Thread(new ThreadStart(processor.process));
                thread.Start();
                Thread.Sleep(1);
            }
        }

        public void stop()
        {
            is_active = false;
        }

        public abstract void handleGETRequest(HttpProcessor p);
        public abstract void handlePOSTRequest(HttpProcessor p, StreamReader inputData);
    }

    public class MyHttpServer : HttpServer {
        public MyHttpServer(int port)
            : base(port) {
        }

        public static uint GetLongTime(DateTime time)
        {
            DateTime time197011 = new DateTime(1970, 1, 1);
            //DateTime time = DateTime.Now;
            TimeSpan ts = time - time197011;
            TimeZone localZone = TimeZone.CurrentTimeZone;
            TimeSpan off = localZone.GetUtcOffset(time);
            ts -= off;

            return (uint)ts.TotalSeconds;
        }

        public static DateTime ConverToTime(int time)
        {
            DateTime time197011 = new DateTime(1970, 1, 1);
            time197011 = time197011.AddSeconds(time);
            TimeZone localZone = TimeZone.CurrentTimeZone;
            TimeSpan ts = localZone.GetUtcOffset(time197011);
            time197011 += ts;

            return time197011;
        }

        char[] root_key = { 'U', 'V', 'W', 'X', 'a', 'b', 'c', 'd' };
        void encrypt_group(byte[] inarray, out byte[] outarray, byte key)
        {
            int i;
            outarray = new byte[8];
            for (i = 0; i < 7; i++)
            {
                outarray[i] = (byte)(inarray[i] ^ key);
            }
            outarray[7] = key;

        }

        void encrypt(byte[] inarray, out byte[] outarray, int size)
        {
            int i;
            int n = (size / 8 + 1);
            outarray = new byte[256];
            for (i = 0; i < n; i++)
            {
                int j = i % 8;
                byte new_key = (byte)(root_key[j] ^ inarray[i * 8 + 7]);
                byte[] f = new byte[8];
                Array.Copy(inarray, i * 8, f, 0, 8);
                byte[] e = new byte[8];


                encrypt_group(f, out e, new_key);
                Array.Copy(e, 0, outarray, i * 8, 8);
            }
        }
        void decrypt_group(byte[] inarray, out byte[] outarray)
        {
            int i;
            outarray = new byte[8];
            for (i = 0; i < 7; i++)
            {
                outarray[i] = (byte)(inarray[i] ^ inarray[7]);
            }
        }

        void decrypt(byte[] inarray, out byte[] outarray, int size)
        {
            int i;
            int n = (size / 8 + 1);
            outarray = new byte[256];
            for (i = 0; i < n; i++)
            {
                int j = i % 8;
                byte[] f = new byte[8];
                Array.Copy(inarray, i * 8, f, 0, 8);
                byte[] e = new byte[8];

                decrypt_group(f, out e);
                Array.Copy(e, 0, outarray, i * 8, 8);

                outarray[i * 8 + 7] = (byte)(root_key[j] ^ inarray[i * 8 + 7]);
            }
        }

        private string GenKey()
        {


            //string str ="1430875657";
            string str = GetLongTime(DateTime.Now).ToString();
            byte[] dd = System.Text.Encoding.UTF8.GetBytes(str);

            byte[] ff = new byte[256];
            Array.Copy(dd, ff, dd.Length);
            byte[] ee = new byte[256];
            //RWPS[TVccaVVVVVV      1430875657

            encrypt(ff, out ee, dd.Length);
            string strresult = System.Text.Encoding.UTF8.GetString(ee);
            strresult = strresult.Replace("\0", "");
            byte[] cc = System.Text.Encoding.UTF8.GetBytes(strresult);
            Array.Copy(cc, ff, cc.Length);
            Array.Clear(ee, 0, ee.Length);
            decrypt(ff, out ee, cc.Length);
            string strroriginal = System.Text.Encoding.UTF8.GetString(ee);
            strroriginal = strroriginal.Substring(0, 10);
            strresult = HttpUtility.UrlEncode(strresult, System.Text.Encoding.UTF8);
            return strresult;
        }

        public override void handleGETRequest (HttpProcessor p)
		{

			if (p.http_url.Equals ("/Test.png")) {
				Stream fs = File.Open("../../Test.png",FileMode.Open);
                
				p.writeSuccess("image/png",fs.Length);
                p.outputStream.Flush();
				fs.CopyTo (p.outputStream.BaseStream);
				p.outputStream.BaseStream.Flush ();
                fs.Close();
                return;
			}

            
			if (p.http_url.StartsWith ("/m3u8")) {
                string path = "http://scgd-m3u8.joyseetv.com:10009/";
                path += p.http_url;
                path += GenKey();
                // The downloaded resource ends up in the variable named content.
          
                // Initialize an HttpWebRequest for the current URL.
                var webReq = (HttpWebRequest)WebRequest.Create(path);

                webReq.UserAgent = "Android/TVPlayerSDK/1.0";
                WebResponse response = webReq.GetResponse();
                Stream fs = webReq.GetResponse().GetResponseStream();

                p.writeSuccess("application/vnd.apple.mpegurl", response.ContentLength);
                p.outputStream.Flush();
				fs.CopyTo (p.outputStream.BaseStream);
				p.outputStream.BaseStream.Flush ();
                fs.Close();
                return;
			}

            Console.WriteLine("request: {0}", p.http_url);
            p.writeSuccess();
            p.outputStream.WriteLine("<html><body><h1>test server</h1>");
            p.outputStream.WriteLine("Current Time: " + DateTime.Now.ToString());
            p.outputStream.WriteLine("url : {0}", p.http_url);

            p.outputStream.WriteLine("<form method=post action=/form>");
            p.outputStream.WriteLine("<input type=text name=foo value=foovalue>");
            p.outputStream.WriteLine("<input type=submit name=bar value=barvalue>");
            p.outputStream.WriteLine("</form>");
        }

        public override void handlePOSTRequest(HttpProcessor p, StreamReader inputData) {
            Console.WriteLine("POST request: {0}", p.http_url);
            string data = inputData.ReadToEnd();

            p.writeSuccess();
            p.outputStream.WriteLine("<html><body><h1>test server</h1>");
            p.outputStream.WriteLine("<a href=/test>return</a><p>");
            p.outputStream.WriteLine("postbody: <pre>{0}</pre>", data);
            

        }
    }

    public class TestMain {
        public static int Main(String[] args) {
            HttpServer httpServer;
            if (args.GetLength(0) > 0) {
                httpServer = new MyHttpServer(Convert.ToInt16(args[0]));
            } else {
                httpServer = new MyHttpServer(12345);
            }
            Thread thread = new Thread(new ThreadStart(httpServer.listen));
            thread.Start();
            
            return 0;
        }

    }

}



