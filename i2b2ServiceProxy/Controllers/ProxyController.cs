using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Dapper;
using System.Data;
using System.Data.SqlClient;
using System.Linq;

using System.Xml.Linq;
using Microsoft.Extensions.Configuration;
using System;
using Microsoft.AspNetCore.Http;
using System.IO;
using System.Threading.Tasks;

namespace i2b2ServiceProxy.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class ProxyController : ControllerBase
    {
        private readonly IConfiguration _configuration;


        public ProxyController(IConfiguration configuration)
        {
            _configuration = configuration;
        }



        [HttpGet()]
        [Produces("application/xml")]
        public ContentResult Get()
        {
            return new ContentResult
            {
                ContentType = "application/xml",
                Content = "<Hello>World. Post if you want data.</Hello>",
                StatusCode = 200
            };
        }


        [Produces("application/xml")]
        [HttpPost("RequestData", Name = "RequestData")]
        [Route("proxy/requestdata")]
        public async Task<ContentResult> RequestDataAsync()
        {
            using var reader = new StreamReader(
                         Request.Body,
                     encoding: System.Text.Encoding.UTF8,
                     detectEncodingFromByteOrderMarks: false
                     );
            var bodyString = await reader.ReadToEndAsync();

            auth_data _auth_data = new auth_data();
            XDocument xml = XDocument.Parse((string)bodyString);
            _auth_data.Request = xml;
            _auth_data.IPAddress = xml.Descendants("client_ip").First().Value;
            auth_return _auth_return = Authenticate(_auth_data);
            if (_auth_return.error != "0")
            {

                return new ContentResult
                {
                    ContentType = "application/xml",
                    Content = _auth_return.auth_err_msg,
                    StatusCode = 200
                };

            }




             Uri clienturi = new Uri(Request.GetTypedHeaders().Referer.AbsoluteUri.ToLower());
            //debug if posting from postman tool
            //Uri clienturi = new Uri("http://services.i2b2.org/i2b2/services/QueryToolService/request".ToLower());

            response_data _response_data = new response_data();
            _response_data.IPAddress = _auth_data.IPAddress;
            _response_data.Service = clienturi.Segments[Array.FindIndex(clienturi.Segments, x => x.Contains("services/")) + 1].Replace("/", "");
            _response_data.Operation = clienturi.Segments[Array.FindIndex(clienturi.Segments, x => x.Contains("services/")) + 2].Replace("/", "");

            _response_data.Request = _auth_data.Request;
            _response_data.UserID = _auth_return.username;





            return new ContentResult
            {
                ContentType = "application/xml",
                Content = GetResponse(_response_data),
                StatusCode = 200
            };
        }


        private string GetResponse(response_data data)
        {
            response_return _response_return;

            try
            {
                using IDbConnection db = new SqlConnection(_configuration.GetConnectionString("CQ2"));
                try
                {
                    db.Open();

                }
                catch (Exception ex)
                {

                    return $"Could not connect\n<pre>{ex.Message}</pre>";
                }

                var p = new DynamicParameters();
                p.Add("@Service", data.Service, dbType: DbType.String);
                p.Add("@Operation", data.Operation, dbType: DbType.String);
                p.Add("@Request", Fix_xml(data.Request).ToString(), dbType: DbType.String);
                p.Add("@UserID", data.UserID, dbType: DbType.String);
                p.Add("@IPAddress", data.IPAddress, dbType: DbType.String);

                _response_return = new response_return();
                _response_return = db.Query<response_return>("HIVE.uspGetResponse", p, commandType: CommandType.StoredProcedure).First();
            }
            catch (Exception e)
            {
                Console.WriteLine(e.Message);
                return $"Error in loading or getting response from HIVE.uspGetResponse\n{e.Message}";

            }
            if (_response_return.ErrorNumber != "0")
                return "Error in loading or getting response from HIVE.uspGetResponse\n";

            return _response_return.ResponseXML.ToString();
        }
        private auth_return Authenticate(auth_data data)
        {
            auth_return _auth_return = new auth_return();



            try
            {
                using IDbConnection db = new SqlConnection(_configuration.GetConnectionString("CQ2"));
                try
                {
                    db.Open();

                }
                catch (Exception ex)
                {
                    _auth_return.auth_err_msg = $"{ex.Message}";
                }

                var p = new DynamicParameters();
                p.Add("@Request", Fix_xml(data.Request).ToString(), dbType: DbType.String);
                p.Add("@IPAddress", data.IPAddress, dbType: DbType.String);
                _auth_return = new auth_return();
                _auth_return = db.Query<auth_return>("PM.uspGetAuthenticate", p, commandType: CommandType.StoredProcedure).First();
            }
            catch (Exception e)
            {
                Console.WriteLine(e.Message);
                _auth_return.auth_err_msg = $"Error in executing statement 3.\n{e.Message}";

            }

            if (_auth_return.trycustomauthenticate != "0")
            { //put custom auth code here
            }

            return _auth_return;




        }
        private string Auth_err_msg()
        {
            return "<ns2:response xmlns:ns2=\"http://www.i2b2.org/xsd/hive/msg/1.1/\" xmlns:ns4=\"http://www.i2b2.org/xsd/cell/pm/1.1/\" xmlns:ns3=\"http://www.i2b2.org/xsd/hive/msg/version/\" xmlns:tns=\"http://ws.pm.i2b2.harvard.edu\"><message_header><i2b2_version_compatible>1.1</i2b2_version_compatible><hl7_version_compatible>2.4</hl7_version_compatible><sending_application><application_name>PM Cell</application_name><application_version>1.601</application_version></sending_application><sending_facility><facility_name>i2b2 Hive</facility_name></sending_facility><receiving_application><application_name>PM Cell</application_name><application_version>1.601</application_version></receiving_application><receiving_facility><facility_name>i2b2 Hive</facility_name></receiving_facility><datetime_of_message>2012-02-21T20:19:50.732Z</datetime_of_message><message_control_id><message_num>0t24RR6mE87f55Af4RbdP</message_num><instance_num>1</instance_num></message_control_id><processing_id><processing_id>P</processing_id><processing_mode>I</processing_mode></processing_id><accept_acknowledgement_type>AL</accept_acknowledgement_type><application_acknowledgement_type>AL</application_acknowledgement_type><country_code>US</country_code><project_id>undefined</project_id></message_header><response_header><result_status><status type=\"ERROR\">Username or password does not exist</status></result_status></response_header></ns2:response>";

        }
        private XDocument Fix_xml(XDocument doc)
        {
            doc.Declaration = null;
            return doc;

        }
        private class auth_return
        {
            public string error { get; set; }
            public string authenticated { get; set; }
            public string username { get; set; }
            public string password { get; set; }
            public string trycustomauthenticate { get; set; }
            public string auth_err_msg { get; set; }

        }
        private class auth_data
        {

            public XDocument Request { get; set; }
            public string IPAddress { get; set; }
        }
        private class response_data
        {
            public string Service { get; set; }
            public string Operation { get; set; }
            public XDocument Request { get; set; }
            public string UserID { get; set; }
            public string IPAddress { get; set; }


        }
        private class response_return
        {
            public string ErrorNumber { get; set; }

            public string ResponseXML { get; set; }
        }

    }
}
