﻿/*
 *  Licensed to SharpSoftware under one or more contributor
 *  license agreements. See the NOTICE file distributed with this work for 
 *  additional information regarding copyright ownership.
 * 
 *  SharpSoftware licenses this file to you under the Apache License, 
 *  Version 2.0 (the "License"); you may not use this file except in 
 *  compliance with the License. You may obtain a copy of the License at
 * 
 *       http://www.apache.org/licenses/LICENSE-2.0
 * 
 *  Unless required by applicable law or agreed to in writing, software
 *  distributed under the License is distributed on an "AS IS" BASIS,
 *  WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 *  See the License for the specific language governing permissions and
 *  limitations under the License.
 */

using NUnit.Framework;
using Itinero.LocalGeo;
using Itinero.Graphs.Directed;
using System.IO;
using Itinero.Attributes;
using System.Linq;
using Itinero.Data.Network.Restrictions;
using Itinero.Data.Contracted;
using System.Collections.Generic;
using Itinero.Algorithms.Search.Hilbert;
using Itinero.Data;

namespace Itinero.Test
{
    /// <summary>
    /// Contains tests for the router db extension methods to extract areas.
    /// </summary>
    [TestFixture]
    public class RouterDbExtractAreaTests
    {
        /// <summary>
        /// Tests extracting a boundingbox from network 5.
        /// </summary>
        [Test]
        public void TestExtractBoxNetwork5()
        {
            var routerDb = new RouterDb();
            routerDb.AddSupportedVehicle(Itinero.Osm.Vehicles.Vehicle.Car);
            routerDb.LoadTestNetwork(
                System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream(
                    "Itinero.Test.test_data.networks.network5.geojson"));
            routerDb.Sort();

            // extract.
            routerDb = routerDb.ExtractArea(52.35246589354224f, 6.662435531616211f,
                52.35580134510498f, 6.667134761810303f);

            // check if the vertices have been copied.
            Assert.AreEqual(11, routerDb.Network.VertexCount);

            // check if the vertex data meta collections have been copied.
            MetaCollection<long> metaCollection;
            Assert.IsTrue(routerDb.VertexData.TryGet<long>("node_ids", out metaCollection));
            Assert.AreEqual(11, metaCollection.Count);

            // check if the vertex meta has been copied.
            var vertexMetaIds = new List<uint>(routerDb.VertexMeta);
            Assert.AreEqual(3, vertexMetaIds.Count);
        }

        /// <summary>
        /// Tests extracting a boundingbox from network 14.
        /// </summary>
        [Test]
        public void TestExtractBoxNetwork14()
        {
            var routerDb = new RouterDb();
            routerDb.AddSupportedVehicle(Itinero.Osm.Vehicles.Vehicle.Car);
            routerDb.LoadTestNetwork(
                System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream(
                    "Itinero.Test.test_data.networks.network14.geojson"));
            routerDb.Sort();

            // extract.
            routerDb = routerDb.ExtractArea(51.264969480610056f, 4.794631004333496f,
                51.26752715540532f, 4.798053503036499f);

            // check if the vertices have been copied.
            Assert.AreEqual(6, routerDb.Network.VertexCount);

            // check restrictions.
            var restrictions = new List<RestrictionsDbMeta>(routerDb.RestrictionDbs);
            Assert.AreEqual(1, restrictions.Count);
            Assert.AreEqual(2, restrictions[0].RestrictionsDb.Count);
        }
    }
}