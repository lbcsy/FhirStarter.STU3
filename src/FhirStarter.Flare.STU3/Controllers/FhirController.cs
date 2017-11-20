﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Web.Http;
using System.Web.Http.Cors;
using System.Xml.Linq;
using FhirStarter.Bonfire.STU3.Interface;
using FhirStarter.Bonfire.STU3.Service;
using FhirStarter.Bonfire.STU3.Validation;
using Hl7.Fhir.Model;
using Hl7.Fhir.Rest;
using Hl7.Fhir.Serialization;
using Spark.Engine.Core;
using Spark.Engine.Extensions;
using Spark.Engine.Infrastructure;

namespace FhirStarter.Flare.STU3.Controllers
{
    [RoutePrefix("fhir"), EnableCors("*", "*", "*", "*")]
    [RouteDataValuesOnly]
    [Bonfire.STU3.Filter.ExceptionFilter]
    public class FhirController : ApiController
    {
        private readonly ICollection<IFhirService> _fhirServices;
        private readonly ICollection<IFhirMockupService> _fhirMockupServices;
        private readonly AbstractStructureDefinitionService _abstractStructureDefinitionService;
        private readonly ServiceHandler _handler = new ServiceHandler();
        private readonly ProfileValidator _profileValidator;

        public FhirController(ICollection<IFhirService> services, ICollection<IFhirMockupService> mockupServices, ProfileValidator profileValidator, AbstractStructureDefinitionService abstractStructureDefinitionService)
        {
            _fhirServices = services;
            _fhirMockupServices = mockupServices;
            _profileValidator = profileValidator;
            _abstractStructureDefinitionService = abstractStructureDefinitionService;
        }

        public FhirController(ICollection<IFhirService> services, ProfileValidator profileValidator, AbstractStructureDefinitionService abstractStructureDefinitionService)
        {
            _fhirServices = services;
            _profileValidator = profileValidator;
            _abstractStructureDefinitionService = abstractStructureDefinitionService;
        }

        public FhirController(ICollection<IFhirService> services, ICollection<IFhirMockupService> mockupServices, AbstractStructureDefinitionService abstractStructureDefinitionService)
        {
            _fhirServices = services;
            _fhirMockupServices = mockupServices;
            _abstractStructureDefinitionService = abstractStructureDefinitionService;
        }

        public FhirController(ICollection<IFhirService> services, AbstractStructureDefinitionService abstractStructureDefinitionService)
        {
            _fhirServices = services;
            _abstractStructureDefinitionService = abstractStructureDefinitionService;
        }

        public FhirController(ICollection<IFhirService> services, ICollection<IFhirMockupService> mockupServices)
        {
            _fhirServices = services;
            _fhirMockupServices = mockupServices;
        }

        public FhirController(ICollection<IFhirService> services)
        {
            _fhirServices = services;

        }

        [HttpGet, Route("StructureDefinition/{nspace}/{id}")]
        public HttpResponseMessage GetStructureDefinition(string nspace, string id)
        {
            var structureDefinition = Load(false, id,nspace);
            if (structureDefinition == null)
                throw new FhirOperationException($"{nameof(StructureDefinition)} for {nspace}/{id} not found",
                    HttpStatusCode.InternalServerError);
            return SendResponse(structureDefinition);
        }

        [HttpGet, Route("StructureDefinition/{id}")]
        public HttpResponseMessage GetStructureDefinition(string id)
        {
            var structureDefinition = Load(false,id);
            if (structureDefinition == null)
                throw new FhirOperationException($"{nameof(StructureDefinition)} for {id} not found",
                    HttpStatusCode.InternalServerError);
            return SendResponse(structureDefinition);
        }

        private StructureDefinition Load(bool excactMatch, string id, string nspace = null)
        {
            string lookup;
            if (string.IsNullOrEmpty(nspace))
            {
                lookup = id;
            }
            else
            {
                lookup = nspace + "/" + id;
            }
            var structureDefinitions = _abstractStructureDefinitionService.GetStructureDefinitions();
            return excactMatch ? structureDefinitions.FirstOrDefault(definition => definition.Type.Equals(lookup)) : structureDefinitions.FirstOrDefault(definition => definition.Url.EndsWith(lookup));
            
        }

        [HttpGet, Route("{type}/{id}"), Route("{type}/identifier/{id}")]
        public HttpResponseMessage Read(string type, string id)
        {
            if (type.Equals(nameof(OperationDefinition)))
            {
                var operationDefinitions = _handler.GetOperationDefinitions(id, _fhirServices);
                return SendResponse(operationDefinitions);
            }

            var service = _handler.FindServiceFromList(_fhirServices, _fhirMockupServices, type);
            var result = service.Read(id);

            return SendResponse(result);
        }

       
        [HttpGet, Route("{type}")]
        public HttpResponseMessage Read(string type)
        {
            var service = _handler.FindServiceFromList(_fhirServices, _fhirMockupServices, type);
            var parameters = Request.GetSearchParams();
            if (!(parameters.Parameters.Count > 0)) return new HttpResponseMessage(HttpStatusCode.ExpectationFailed);
            var results = service.Read(parameters);
            return SendResponse(results);
        }

        [HttpGet, Route("")]
        // ReSharper disable once InconsistentNaming
        public HttpResponseMessage Query(string _query)
        {
            var searchParams = Request.GetSearchParams();
            var service = _handler.FindServiceFromList(_fhirServices, _fhirMockupServices, searchParams.Query);
            var result = service.Read(searchParams);

            return SendResponse(result);
        }

        [HttpPost, Route("{type}")]
        public HttpResponseMessage Create(string type, Resource resource)
        {
            var xml = FhirSerializer.SerializeToXml(resource);
            var service = _handler.FindServiceFromList(_fhirServices, _fhirMockupServices, type);
            
            resource = (Resource) ValidateResource(resource);
            if (resource is OperationOutcome)
            {
                return SendResponse(resource);
            }
            return _handler.ResourceCreate(type, resource, service);
        }

        [HttpPut, Route("{type}/{id}")]
        public HttpResponseMessage Update(string type, string id, Resource resource)
        {
            var service = _handler.FindServiceFromList(_fhirServices, _fhirMockupServices, type);
            return _handler.ResourceUpdate(type, id, resource, service);
        }

        [HttpDelete, Route("{type}/{id}")]
        public HttpResponseMessage Delete(string type, string id)
        {
            var service = _handler.FindServiceFromList(_fhirServices, _fhirMockupServices, type);
            return _handler.ResourceDelete(type, Key.Create(type, id), service);
        }

        [HttpPatch, Route("{type}/{id}")]
        public HttpResponseMessage Patch(string type, string id, Resource resource)
        {
            var service = _handler.FindServiceFromList(_fhirServices, _fhirMockupServices, type);
            return _handler.ResourcePatch(type, Key.Create(type, id), resource, service);
        }

        private HttpResponseMessage SendResponse(Base resource)
        {
           
            var headers = Request.Headers;
            var accept = headers.Accept;
            var returnJson = ReturnJson(accept);
            if (!(resource is OperationOutcome))
            {
                resource = ValidateResource((Resource)resource);
            }
            

            StringContent httpContent;
            if (!returnJson)
            {
                var xml = FhirSerializer.SerializeToXml(resource);
                httpContent =
                    new StringContent(xml, Encoding.UTF8,
                     FhirMediaType.XmlResource);
            }
            else
            {
                httpContent =
                    new StringContent(FhirSerializer.SerializeToJson(resource), Encoding.UTF8,
                     FhirMediaType.JsonResource);
            }
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = httpContent };
            return response;
        }

        private Base ValidateResource(Resource resource)
        {

            if (_profileValidator == null) return resource;
            if (resource is OperationOutcome) return resource;
            
            {
                var resourceName = resource.TypeName;
                var structureDefinition = Load(true, resourceName);
                if (structureDefinition != null)
                {
                    var found = resource.Meta != null && resource.Meta.ProfileElement.Count == 1 &&
                                resource.Meta.ProfileElement[0].Value.Equals(structureDefinition.Url);
                    if (!found)
                    {
                        throw new ArgumentException($"Profile for {resourceName} must be set to: {structureDefinition.Url}");
                    }
                }
                
            }

            var resourceAsXDocument = XDocument.Parse(FhirSerializer.SerializeToXml(resource));
            var validationResult = _profileValidator.Validate(resource, true, true);
            if (validationResult.Issue.Count > 0)
            {
                resource = validationResult;
            }


            return resource;
        }

        private static bool ReturnJson(HttpHeaderValueCollection<MediaTypeWithQualityHeaderValue> accept)
        {
            var jsonHeaders = ContentType.JSON_CONTENT_HEADERS;
            var returnJson = false;
            foreach (var x in accept)
            {
                if (jsonHeaders.Any(y => x.MediaType.Contains(y)))
                {
                    returnJson = true;
                }
            }
            return returnJson;
        }

        [HttpGet, Route("metadata")]
        public HttpResponseMessage MetaData()
        {
            var headers = Request.Headers;
            var accept = headers.Accept;
            var returnJson = accept.Any(x => x.MediaType.Contains(FhirMediaType.HeaderTypeJson));

            StringContent httpContent;
            var metaData = _handler.CreateMetadata(_fhirServices, _abstractStructureDefinitionService, Request.RequestUri.AbsoluteUri);
            if (!returnJson)
            {
                var xml = FhirSerializer.SerializeToXml(metaData);
                httpContent =
                    new StringContent(xml, Encoding.UTF8,
                        "application/xml");
            }
            else
            {
                httpContent =
                    new StringContent(FhirSerializer.SerializeToJson(metaData), Encoding.UTF8,
                        "application/json");
            }
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = httpContent };
            return response;
        }
    }
}
