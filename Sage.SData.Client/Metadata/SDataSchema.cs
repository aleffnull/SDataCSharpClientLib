﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
using System.Xml.Schema;

namespace Sage.SData.Client.Metadata
{
    public class SDataSchema : SDataSchemaObject
    {
        private IList<XmlSchemaImport> _imports;
        private IList<XmlQualifiedName> _namespaces;
        private SDataSchemaKeyedObjectCollection<SDataSchemaType> _types;

        public string Id { get; set; }
        public string TargetNamespace { get; set; }
        public string Version { get; set; }
        public XmlSchemaForm ElementFormDefault { get; set; }

        public override IEnumerable<SDataSchemaObject> Children
        {
            get { return _types.Cast<SDataSchemaObject>(); }
        }

        public IList<XmlQualifiedName> Namespaces
        {
            get { return _namespaces ?? (_namespaces = new List<XmlQualifiedName>()); }
        }

        public IList<XmlSchemaImport> Imports
        {
            get { return _imports ?? (_imports = new List<XmlSchemaImport>()); }
        }

        public SDataSchemaKeyedObjectCollection<SDataSchemaType> Types
        {
            get { return _types ?? (_types = new SDataSchemaKeyedObjectCollection<SDataSchemaType>(this, type => type.Name)); }
        }

        public SDataSchemaKeyedEnumerable<SDataSchemaSimpleType> SimpleTypes
        {
            get { return new SDataSchemaKeyedEnumerable<SDataSchemaSimpleType>(Types.OfType<SDataSchemaSimpleType>(), type => type.Name); }
        }

        public SDataSchemaKeyedEnumerable<SDataSchemaEnumType> EnumTypes
        {
            get { return new SDataSchemaKeyedEnumerable<SDataSchemaEnumType>(Types.OfType<SDataSchemaEnumType>(), type => type.Name); }
        }

        public SDataSchemaKeyedEnumerable<SDataSchemaComplexType> ComplexTypes
        {
            get { return new SDataSchemaKeyedEnumerable<SDataSchemaComplexType>(Types.OfType<SDataSchemaComplexType>(), type => type.Name); }
        }

        public SDataSchemaKeyedEnumerable<SDataSchemaResourceType> ResourceTypes
        {
            get { return new SDataSchemaKeyedEnumerable<SDataSchemaResourceType>(Types.OfType<SDataSchemaResourceType>(), type => type.ElementName); }
        }

        public SDataSchemaKeyedEnumerable<SDataSchemaServiceOperationType> ServiceOperationTypes
        {
            get { return new SDataSchemaKeyedEnumerable<SDataSchemaServiceOperationType>(Types.OfType<SDataSchemaServiceOperationType>(), type => type.ElementName); }
        }

        public SDataSchemaKeyedEnumerable<SDataSchemaNamedQueryType> NamedQueryTypes
        {
            get { return new SDataSchemaKeyedEnumerable<SDataSchemaNamedQueryType>(Types.OfType<SDataSchemaNamedQueryType>(), type => type.ElementName); }
        }

        public static SDataSchema Read(Stream stream)
        {
            return BuildReadSchema(XmlSchema.Read(stream, null));
        }

        public static SDataSchema Read(TextReader reader)
        {
            return BuildReadSchema(XmlSchema.Read(reader, null));
        }

        public static SDataSchema Read(XmlReader reader)
        {
            return BuildReadSchema(XmlSchema.Read(reader, null));
        }

        private static SDataSchema BuildReadSchema(XmlSchema xmlSchema)
        {
            var schema = new SDataSchema();
            schema.Read(xmlSchema);
            return schema;
        }

        public void Write(Stream stream)
        {
            BuildWriteSchema().Write(stream);
        }

        public void Write(TextWriter writer)
        {
            BuildWriteSchema().Write(writer);
        }

        public void Write(XmlWriter writer)
        {
            BuildWriteSchema().Write(writer);
        }

        private XmlSchema BuildWriteSchema()
        {
            var xmlSchema = new XmlSchema();
            Write(xmlSchema);
            return xmlSchema;
        }

        protected internal override void Read(XmlSchemaObject obj)
        {
            var xmlSchema = (XmlSchema) obj;
            Id = xmlSchema.Id;
            TargetNamespace = xmlSchema.TargetNamespace;
            Version = xmlSchema.Version;
            ElementFormDefault = xmlSchema.ElementFormDefault;

            foreach (var ns in xmlSchema.Namespaces.ToArray())
            {
                Namespaces.Add(ns);
            }

            foreach (var import in xmlSchema.Includes.OfType<XmlSchemaImport>())
            {
                Imports.Add(import);
            }

            XmlSchemaElement lastElement = null;
            SDataSchemaComplexType lastComplexType = null;
            XmlSchemaComplexType lastComplexList = null;

            foreach (var item in xmlSchema.Items)
            {
                SDataSchemaType type;

                if (item is XmlSchemaElement)
                {
                    lastElement = (XmlSchemaElement) item;
                    lastComplexType = null;
                    lastComplexList = null;
                    continue;
                }

                if (item is XmlSchemaComplexType)
                {
                    var xmlComplexType = (XmlSchemaComplexType) item;

                    if (xmlComplexType.Particle == null || xmlComplexType.Particle is XmlSchemaAll)
                    {
                        SDataSchemaComplexType complexType;

                        if (lastElement != null && lastElement.SchemaTypeName == new XmlQualifiedName(xmlComplexType.Name, TargetNamespace))
                        {
                            var roleAttr = lastElement.UnhandledAttributes != null
                                               ? lastElement.UnhandledAttributes.FirstOrDefault(attr => attr.NamespaceURI == SmeNamespaceUri && attr.LocalName == "role")
                                               : null;

                            if (roleAttr == null)
                            {
                                throw new NotSupportedException();
                            }

                            switch (roleAttr.Value)
                            {
                                case "resourceKind":
                                    complexType = new SDataSchemaResourceType();
                                    break;
                                case "serviceOperation":
                                    complexType = new SDataSchemaServiceOperationType();
                                    break;
                                case "query":
                                    complexType = new SDataSchemaNamedQueryType();
                                    break;
                                default:
                                    throw new NotSupportedException();
                            }

                            complexType.Read(lastElement);
                        }
                        else
                        {
                            complexType = new SDataSchemaComplexType();
                        }

                        type = complexType;
                        lastElement = null;

                        if (lastComplexList != null)
                        {
                            var sequence = (XmlSchemaSequence) lastComplexList.Particle;

                            if (sequence.Items.Count != 1)
                            {
                                throw new NotSupportedException();
                            }

                            var element = sequence.Items[0] as XmlSchemaElement;

                            if (element == null || element.SchemaTypeName != new XmlQualifiedName(xmlComplexType.Name, TargetNamespace))
                            {
                                throw new NotSupportedException();
                            }

                            complexType.ListName = lastComplexList.Name;
                            complexType.ListItemName = element.Name;
                            complexType.ListAnyAttribute = lastComplexList.AnyAttribute;
                            lastComplexList = null;
                        }
                        else
                        {
                            lastComplexType = complexType;
                        }
                    }
                    else if (xmlComplexType.Particle is XmlSchemaSequence)
                    {
                        if (lastComplexType != null)
                        {
                            var sequence = (XmlSchemaSequence) xmlComplexType.Particle;

                            if (sequence.Items.Count != 1)
                            {
                                throw new NotSupportedException();
                            }

                            var element = sequence.Items[0] as XmlSchemaElement;

                            if (element == null || element.SchemaTypeName != lastComplexType.QualifiedName)
                            {
                                throw new NotSupportedException();
                            }

                            lastComplexType.ListName = xmlComplexType.Name;
                            lastComplexType.ListItemName = element.Name;
                            lastComplexType.ListAnyAttribute = xmlComplexType.AnyAttribute;
                            lastComplexType = null;
                        }
                        else
                        {
                            lastComplexList = xmlComplexType;
                        }

                        continue;
                    }
                    else if (xmlComplexType.Particle is XmlSchemaChoice)
                    {
                        type = new SDataSchemaChoiceType();
                    }
                    else
                    {
                        throw new NotSupportedException();
                    }
                }
                else if (item is XmlSchemaSimpleType)
                {
                    var simpleType = (XmlSchemaSimpleType) item;
                    var restriction = simpleType.Content as XmlSchemaSimpleTypeRestriction;

                    if (restriction == null)
                    {
                        throw new NotSupportedException();
                    }

                    if (restriction.Facets.Cast<XmlSchemaObject>().All(facet => facet is XmlSchemaEnumerationFacet))
                    {
                        type = new SDataSchemaEnumType();
                    }
                    else
                    {
                        type = new SDataSchemaSimpleType();
                    }
                }
                else
                {
                    throw new NotSupportedException();
                }

                type.Read(item);
                Types.Add(type);
            }

            Compile();
        }

        protected internal override void Write(XmlSchemaObject obj)
        {
            var xmlSchema = (XmlSchema) obj;
            xmlSchema.Id = Id;
            xmlSchema.TargetNamespace = TargetNamespace;
            xmlSchema.Version = Version;
            xmlSchema.ElementFormDefault = ElementFormDefault;

            var hasSme = false;
            var hasXs = false;
            var hasDefault = false;

            foreach (var ns in Namespaces)
            {
                xmlSchema.Namespaces.Add(ns.Name, ns.Namespace);
                hasSme |= ns.Namespace == SmeNamespaceUri;
                hasXs |= ns.Namespace == XmlSchema.Namespace;
                hasDefault |= ns.Namespace == TargetNamespace;
            }

            foreach (var import in Imports)
            {
                xmlSchema.Includes.Add(import);
            }

            if (!hasSme)
            {
                xmlSchema.Namespaces.Add("sme", SmeNamespaceUri);
            }

            if (!hasXs)
            {
                xmlSchema.Namespaces.Add("xs", XmlSchema.Namespace);
            }

            if (!hasDefault)
            {
                xmlSchema.Namespaces.Add(string.Empty, TargetNamespace);
            }

            foreach (var type in Types)
            {
                if (type is SDataSchemaComplexType)
                {
                    var complexType = (SDataSchemaComplexType) type;

                    if (type is SDataSchemaTopLevelType)
                    {
                        var element = new XmlSchemaElement {SchemaTypeName = complexType.QualifiedName};
                        type.Write(element);
                        xmlSchema.Items.Add(element);
                    }

                    var xmlComplexType = new XmlSchemaComplexType();
                    type.Write(xmlComplexType);
                    xmlSchema.Items.Add(xmlComplexType);

                    if (complexType.ListName != null)
                    {
                        xmlComplexType = new XmlSchemaComplexType
                                         {
                                             Name = complexType.ListName,
                                             AnyAttribute = complexType.ListAnyAttribute,
                                             Particle = new XmlSchemaSequence
                                                        {
                                                            Items =
                                                                {
                                                                    new XmlSchemaElement
                                                                    {
                                                                        Name = complexType.ListItemName,
                                                                        SchemaTypeName = complexType.QualifiedName,
                                                                        MinOccurs = 0,
                                                                        MaxOccurs = decimal.MaxValue
                                                                    }
                                                                }
                                                        }
                                         };
                        xmlSchema.Items.Add(xmlComplexType);
                    }
                }
                else if (type is SDataSchemaChoiceType)
                {
                    var xmlComplexType = new XmlSchemaComplexType();
                    type.Write(xmlComplexType);
                    xmlSchema.Items.Add(xmlComplexType);
                }
                else if (type is SDataSchemaSimpleType || type is SDataSchemaEnumType)
                {
                    var xmlType = new XmlSchemaSimpleType();
                    type.Write(xmlType);
                    xmlSchema.Items.Add(xmlType);
                }
                else
                {
                    throw new NotSupportedException();
                }
            }
        }

        private void Compile()
        {
            var listTypes = ComplexTypes.Where(type => type.ListName != null)
                .Select(type => new {name = type.ListQualifiedName, type = (SDataSchemaType) type});
            var types = Types.Select(type => new {name = type.QualifiedName, type})
                .Concat(listTypes)
                .ToDictionary(type => type.name, type => type.type);
            Compile(types);
        }

        internal void Compile(IDictionary<XmlQualifiedName, SDataSchemaType> types)
        {
            foreach (var typeRef in Descendents().OfType<SDataSchemaTypeReference>())
            {
                SDataSchemaType type;

                if (types.TryGetValue(typeRef.QualifiedName, out type))
                {
                    typeRef.SchemaType = type;
                }
            }
        }
    }
}