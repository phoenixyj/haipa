﻿//using System;
//using System.Linq;
//using HyperVPlus.StateDb;
//using Microsoft.AspNet.OData;
//using Microsoft.AspNetCore.Mvc;

//namespace Haipa.Api.Controllers
//{
//    public class OperationSetController : ODataController
//    {
//        private readonly StateStoreContext _db;

//        public OperationSetController(StateStoreContext context)
//        {
//            _db = context;
//        }

//        [EnableQuery]
//        public IActionResult Get()
//        {

//            return Ok(_db.Operations);
//        }
     
//        [EnableQuery]
//        public IActionResult Get(Guid key)
//        {
//            return Ok(SingleResult.Create(_db.Operations.Where(c => c.Id == key)));
//        }


//        [EnableQuery]
//        public IActionResult GetLogEntries([FromODataUri] Guid key)
//        {
//            var op = _db.Operations.FirstOrDefault(x => x.Id == key);
//            if (op == null) return Ok();

//            return Ok(_db.Logs.Where(x => x.Operation == op));
//        }

//    }
//}