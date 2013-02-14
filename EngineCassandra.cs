using FluentCassandra;
using FluentCassandra.Linq;
using sema2012m;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace EngineCassandra
{
    public class EngineCassandra : Engine
    {
        public static List<string> inversesTypes;
        class FormatDictionaries
        {
            public Dictionary<string, XElement> Fields { get; set; }
            public Dictionary<string, FormatDictionaries> Directs { get; set; }
            public Dictionary<string, FormatDictionaries> Inverses { get; set; }
            public FormatDictionaries(XElement formatRecord)
            {
                                                                                                
                Fields = formatRecord.Elements("field")
                               .ToDictionary(f => f.Attribute("prop").Value.Split('/').Last());
                Directs = formatRecord.Elements("direct")
                                .ToDictionary(f => f.Attribute("prop").Value.Split('/').Last(), f=> new FormatDictionaries(f.Elements().First()));
                Inverses = formatRecord.Elements("direct")
                                 .ToDictionary(f => f.Attribute("prop").Value.Split('/').Last(), f => new FormatDictionaries(f.Elements().First()));
            }
        }
        static readonly Dictionary<string, FormatDictionaries> Formats = XElement.Load(@"C:\Users\Сергей\Documents\GitHub\EngineCassandra\ApplicationProfile.xml")
            .Element("formats")
            .Elements()
            .ToDictionary(f => f.Attribute("fid").Value,
            format => new FormatDictionaries(format));


        public override XElement GetItemByIdBasic(string id, bool addinverse)
        {
            using (var db = new CassandraContext("rdf", "localhost"))
            {
                var cfRDF = db.GetColumnFamily("rdf");
                return GetItemByIdBasic(id, addinverse, true, cfRDF);
            }
        }

        private static XElement GetItemByIdBasic(string id, bool addinverse, bool addDirect, CassandraColumnFamily cfRDF, string excludedId=null, string excludeProp = null)
        {
           
                //  if (!addinverse)
                var iRow = cfRDF.FirstOrDefault(roww => roww.Key == id);
            if (iRow == null) return null;
            var itemType = iRow["type"];

            XElement xitem,
                     xResult = new XElement("record", new XAttribute("fid", itemType),
                                            new XAttribute(ONames.rdfabout, id),
                                            new XAttribute("type", "http://fogid.net/o/" + itemType));
            try
            {
                xitem = XElement.Parse(iRow["XML"]);
            }
            catch
            {
                return xResult;
            }

            if (!addDirect)
                xResult.Add(from xProperty in xitem.Elements()
                            let resource = xProperty.Attribute(ONames.rdfresource)
                            where resource == null
                            select new XElement("field",
                                                new XAttribute("prop", "http://fogid.net/o/" + xProperty.Name),
                                                new XAttribute("value", xProperty.Value)));
            else
            {
                foreach (var xProperty in  xitem.Elements())
                {
                    var resource = xProperty.Attribute(ONames.rdfresource);
                    if (resource == null)
                        xResult.Add(new XElement("field",
                                                 new XAttribute("prop",
                                                                "http://fogid.net/o/" + xProperty.Name),
                                                 new XAttribute("value", xProperty.Value)));
                    else
                    {
                        if (excludedId == resource.Value)
                        {
                            excludeProp = xProperty.Name.ToString();
                            continue;
                        }
                        xResult.Add(new XElement("direct",
                                                 new XAttribute("prop",
                                                                "http://fogid.net/o/" + xProperty.Name),
                                                 GetItemByIdBasic(resource.Value, false, false, cfRDF)));
                    }
                }
            }
            if (addinverse)
            {
                //  string temp;
                // var rows = db.ExecuteQuery(temp = string.Format("SELECT * FROM rdf WHERE KEY=='{0} or " + string.Join(" ", inversesTypes.Select(t => t + "={0}")), id));
                //var rows = db.ExecuteQuery(temp = string.Format("SELECT * FROM rdf WHERE " + string.Join(" ", inversesTypes.Select(t => t + "={0}")), id));
             string property;  
              
                xResult.Add(
                    cfRDF.Where(r => inversesTypes.Any(type => r[type] == id))
                         .Select(row => new XElement("inverse",
                                                            GetItemByIdBasic(row.Key, false, true, cfRDF, id, property),
                                                            new XAttribute("prop", "http://fogid.net/o/" + property))));
            }
            return xResult;
        }
        }

        private static KeyValuePair<XElement, string> XmlRdf2Format(CassandraColumnFamily columnFamyly, ICqlRow row, FormatDictionaries format = null, string exceptedId = null, bool withDirect = false)
        {
            var type = row["type"];
            XElement xitem, xResult = new XElement("record", new XAttribute("fid", type), new XAttribute("type", "http://fogid.net/o/"+type));           
            
             
             try
            {
            xitem = XElement.Parse(row["XML"]);
            }
            catch
            {            
                return new KeyValuePair<XElement,string>(xResult, string.Empty);
            }
            string excludedProperty=string.Empty;
                           
            XElement xPropDefinition;
            FormatDictionaries directProperty;
            if (format == null) format = Formats[type];
            foreach (var xProperty in xitem.Elements())
            {                  
                XAttribute resource = xProperty.Attribute(ONames.rdfresource);
                if (resource == null)
                {                         
                    if ( format.Fields.TryGetValue(xProperty.Name.ToString(), out xPropDefinition))
                    {
                        xPropDefinition.Add(new XAttribute("value", xProperty.Value), xProperty.Attribute("lang"));
                        xResult.Add(xPropDefinition);
                    }      
                }
                else
                {
                    if (withDirect)
                    {                        
                        if (resource.Value == exceptedId) { excludedProperty = xProperty.Name.ToString(); }
                        else
                        {    
                            if (format.Directs.TryGetValue(xProperty.Name.ToString(), out directProperty))
                            {
                                xResult.Add(new XElement("record",
                                                         new XAttribute("type", "http://fogid.net/o/" + xProperty.Name),
                                                         new XAttribute("fid", xProperty.Name),
                                                         XmlRdf2Format(columnFamyly,
                                                                       columnFamyly.FirstOrDefault(
                                                                           r => r.Key == resource.Value), directProperty)
                                                ));
                            }
                        }
                    }
                }
            }
            return new KeyValuePair<XElement,string>(xResult, excludedProperty);
        }

        public override XElement GetItemById(string id, XElement format)
        {
            var cfRDF = db.GetColumnFamily("rdf");
            var iRow = cfRDF.FirstOrDefault(roww => roww.Key == id);
            var type = iRow["type"];

            XElement xResult = XmlRdf2Format(iRow).Key;

            if (addinverse)
            {
                string temp;
                // var rows = db.ExecuteQuery(temp = string.Format("SELECT * FROM rdf WHERE KEY=='{0} or " + string.Join(" ", inversesTypes.Select(t => t + "={0}")), id));
                var rows = db.ExecuteQuery(temp = string.Format("SELECT * FROM rdf WHERE " + string.Join(" ", inversesTypes.Select(t => t + "={0}")), id));
                temp = "";
                foreach (var inversePair in rows.Select(row => XmlRdf2Format(row, exceptedId: id)))
                {
                    xResult.Add(new XElement("inverse", new XAttribute("prop", "http://fogid.net/o/" + inversePair.Value),
                        inversePair.Key));
                }
            }
            return xResult;
        }

    }
}
