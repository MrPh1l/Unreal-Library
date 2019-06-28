﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace UELib.Dummy
{
    public class RLDummyPackageStream : UPackageStream, IUnrealStream
    {
        private const int DummyPackageFlag = 1;
        private const int DummyFileVersion = 868;
        private const int DummyLicenseeVersion = 0;
        private const int DummyEngineVersion = 12791;
        private const int DummyCookerVersion = 0;
        private const int DummyCompression = 0;
        private const int DummyCompressedChunksData = 0;
        private const int DummyUnknown5 = 0;
        private const int DummyUnknownStringArray = 0;
        private const int DummyUnknownTypeArray = 0;

        private const int NameCountPosition = 25;
        private const int ExportCountPosition = 33;
        private const int ImportCountPosition = 41;

        private const int TotalHeaderSizePosition = 8;

        private UnrealPackage package;

        //public uint Version { get; set; }

        public RLDummyPackageStream(UnrealPackage package, string filePath)
        {
            this.package = package;
            var fileStream = File.Open(filePath, FileMode.Create, FileAccess.Write);
            _stream = fileStream;
            Package = package;

            UW = new RLPackageWriter(_stream, DummyEngineVersion);
            MinimalTexture2D.AddNamesToNameTable(package);
            MinimalStaticMesh.AddNamesToNameTable(package);
            MinimalTextureRenderTarget2D.AddNamesToNameTable(package);
            MinimalTextureRenderTargetCube.AddNamesToNameTable(package);
        }

        private const int ExportTableItemSize = 68;
        private const int DependsOffsetPosition = 49;
        private const int ThumbnailDataOffsetPosition = 53;
        private const int ThumbnailTableOffsetPosition = 65;

        public void Serialize()
        {
            initHeader();
            // TODO: Trim these tables down to what the dummy assets actually use.
            // This would require preparsing the export items. Should not be impossible to do.


            SerializeNameTable();
            //SerializeImportsTable();

            List<DummyExportTableItem> exportsToSerialize = GetExportsToSerialize();

            var newNameTable = new List<UNameTableItem>();
            var newImportTable = new List<DummyImportTableItem>();

            //Let's start by trimming down the import table
            TrimImportTable(exportsToSerialize, newImportTable);
            var importOffset = UW.BaseStream.Position;
            WriteIntAtPosition(newImportTable.Count(), ImportCountPosition);
            WriteIntAtPosition((int)importOffset, ImportCountPosition + 4);
            foreach (var import in newImportTable)
            {
                import.Serialize(this);
            }

            //Write info about exports in header
            var exportOffset = UW.BaseStream.Position;
            WriteIntAtPosition(exportsToSerialize.Count(), ExportCountPosition);
            WriteIntAtPosition((int)exportOffset, ExportCountPosition + 4);

            //This comes after the export table, but we need to know it's size so we can set the correct serialOffsets for the export items.
            var thumbnails = new ThumbnailTable();
            thumbnails.Init(exportsToSerialize);
            var thumbnailsTotalSize = thumbnails.GetSerialSize();

            //TODO - understand this table...
            var dependsTotalSize = exportsToSerialize.Count() * 4;

            //Serialize the export table
            SerializeExportTable(exportsToSerialize, thumbnailsTotalSize, dependsTotalSize);

            //Serialize depends table.
            SerializeDependsTable(exportsToSerialize.Count());

            thumbnails.Serialize(this);
            WriteIntAtPosition(thumbnails.thumbnailDataOffset, ThumbnailDataOffsetPosition);
            WriteIntAtPosition(thumbnails.thumbnailTableOffset, ThumbnailTableOffsetPosition);

            WriteIntAtPosition((int)UW.BaseStream.Position, TotalHeaderSizePosition);

            int noneIndex = package.Names.FindIndex((n) => n.Name == "None");
            foreach (var dummyExport in exportsToSerialize)
            {
                var exportObject = dummyExport.original;
                switch (exportObject.ClassName)
                {
                    case "Texture2D":
                        new MinimalTexture2D().Write(this, package);
                        break;

                    case "TextureCube":
                        new MinimalTextureCube().Write(this, package);
                        break;

                    case "TextureRenderTarget2D":
                        new MinimalTextureRenderTarget2D().Write(this, package);
                        break;

                    case "TextureRenderTargetCube":
                        new MinimalTextureRenderTargetCube().Write(this, package);
                        break;
                    case "LightMapTexture2D":
                        new MinimalLightMapTexture2D().Write(this, package);
                        break;
                    case "StaticMesh":
                        new MinimalStaticMesh().Write(this, package);
                        break;

                    case "Material":
                        new MinimalMaterial().Write(this, package);
                        break;

                    case "class":
                        if (exportObject.SerialSize == 0)
                        {
                            break;
                        }
                        break;

                    default:
                        //We just need the netindex and a None FName
                        package.Stream.Position = exportObject.SerialOffset;
                        //NetIndex
                        UW.Write(package.Stream.ReadInt32());
                        //None index and count
                        UW.Write(noneIndex);
                        UW.Write(0);
                        break;
                }
            }
            for (int ii = 0; ii < 250; ii++)
            {
                UW.Write(0);
            }
        }

        private void TrimImportTable(List<DummyExportTableItem> exportsToSerialize, List<DummyImportTableItem> newImportTable)
        {
            foreach (var export in exportsToSerialize)
            {
                export.newClassIndex = AddToImportTable(newImportTable, export, export.newClassIndex);
                export.newSuperIndex = AddToImportTable(newImportTable, export, export.newSuperIndex);
                export.newOuterIndex = AddToImportTable(newImportTable, export, export.newOuterIndex);
                export.newArchetypeIndex = AddToImportTable(newImportTable, export, export.newArchetypeIndex);
            }
        }

        private int FromListIndexToImportTableIndex(int index)
        {
            return -index - 1;
        }

        private int AddToImportTable(List<DummyImportTableItem> newImportTable, DummyExportTableItem export, int importIndex)
        {
            if (importIndex < 0) //import
            {
                var import = package.Imports[-importIndex - 1];
                var newIndex = newImportTable.FindIndex((i) => i.original == import);
                if (newIndex == -1)
                {
                    newImportTable.Add(new DummyImportTableItem(import));
                    newIndex = newImportTable.Count - 1;
                }

                //Process the outer\parent object(s) of the import
                var dummyImport = newImportTable[newIndex];
                while (dummyImport.newOuterIndex < 0)
                {
                    var parentImport = package.Imports[-dummyImport.original.OuterIndex - 1];
                    var newParentIndex = newImportTable.FindIndex((i) => i.original == parentImport);
                    if (newParentIndex == -1)
                    {
                        newImportTable.Add(new DummyImportTableItem(parentImport));
                        newParentIndex = newImportTable.Count - 1;
                        dummyImport.newOuterIndex = FromListIndexToImportTableIndex(newParentIndex);
                        dummyImport = newImportTable[newParentIndex];
                    }
                    else
                    {
                        dummyImport.newOuterIndex = FromListIndexToImportTableIndex(newParentIndex);
                        //We've already added this to the table. And we assume we can break the "recursion" here..
                        break;
                    }

                }
                return FromListIndexToImportTableIndex(newIndex);
            }
            //if export, just return the old one.
            return importIndex;
        }

        private void SerializeExportTable(List<DummyExportTableItem> exportsToSerialize, int thumbnailsTotalSize, int dependsTotalSize)
        {
            int calculatedTotalHeaderSize = (int)(UW.BaseStream.Position + ExportTableItemSize * exportsToSerialize.Count()) + thumbnailsTotalSize + dependsTotalSize;
            int serialOffset = calculatedTotalHeaderSize;
            foreach (var dummyExport in exportsToSerialize)
            {
                switch (dummyExport.original.ClassName)
                {
                    case "Texture2D":
                        DummyExportTableSerialize(dummyExport, serialOffset, MinimalTexture2D.serialSize);
                        serialOffset += MinimalTexture2D.serialSize;
                        break;

                    case "TextureCube":
                        DummyExportTableSerialize(dummyExport, serialOffset, MinimalTextureCube.serialSize);
                        serialOffset += MinimalTextureCube.serialSize;
                        break;
                    case "TextureRenderTarget2D":
                        DummyExportTableSerialize(dummyExport, serialOffset, MinimalTextureRenderTarget2D.serialSize);
                        serialOffset += MinimalTextureRenderTarget2D.serialSize;
                        break;

                    case "TextureRenderTargetCube":
                        DummyExportTableSerialize(dummyExport, serialOffset, MinimalTextureRenderTargetCube.serialSize);
                        serialOffset += MinimalTextureRenderTargetCube.serialSize;
                        break;

                    case "LightMapTexture2D":
                        DummyExportTableSerialize(dummyExport, serialOffset, MinimalLightMapTexture2D.serialSize);
                        serialOffset += MinimalLightMapTexture2D.serialSize;
                        break;
                    case "StaticMesh":
                        DummyExportTableSerialize(dummyExport, serialOffset, MinimalStaticMesh.serialSize);
                        serialOffset += MinimalStaticMesh.serialSize;
                        break;

                    case "Material":
                        DummyExportTableSerialize(dummyExport, serialOffset, MinimalMaterial.serialSize);
                        serialOffset += MinimalMaterial.serialSize;
                        break;

                    case "class":
                        if (dummyExport.original.SerialSize == 0)
                        {
                            //These should really be skipped right??
                            DummyExportTableSerialize(dummyExport, 0, 0);
                        }
                        break;

                    default:
                        DummyExportTableSerialize(dummyExport, serialOffset, DummySerialSize);
                        serialOffset += DummySerialSize;
                        break;
                }
            }
        }

        private void SerializeDependsTable(int exportCount)
        {
            WriteIntAtPosition((int)UW.BaseStream.Position, DependsOffsetPosition);
            for (int i = 0; i < exportCount; i++)
            {
                UW.Write(0);
            }
        }

        private List<DummyExportTableItem> GetExportsToSerialize()
        {
            var packagesNotInUse = package.Exports.Where((e) => e.ClassName == "Package").ToList();
            var exportsToSerialize = new List<DummyExportTableItem>();
            int i = 0;
            foreach (var export in package.Exports)
            {
                //None 
                if (export.SerialSize == 0)
                    continue;
                // any object not a child of package. Don't need it
                if (export.OuterTable != null && export.OuterTable.ClassName != "Package")
                    continue;
                if (export.ClassName == "ObjectReferencer" || export.ClassName == "World")
                    continue;

                if (export.OuterTable?.ClassName == "Package")
                {
                    var outerPackage = export.OuterTable as UExportTableItem;
                    packagesNotInUse.Remove(outerPackage);
                }
                if (i >= 0 && i <= 550)
                {
                    exportsToSerialize.Add(new DummyExportTableItem(export));
                }
                i++;

            }
            foreach (var packageToRemove in packagesNotInUse)
            {
                exportsToSerialize.RemoveAll((e) => e.original == packageToRemove);
            }

            //var exportsToSerialize = package.Exports.Where((e) => e.SerialSize != 0 && (e.OuterTable == null || e.OuterTable.ClassName == "Package") && e.ClassName != "ObjectReferencer")
            //    .Select((e) => new DummyExportTableItem(e))
            //    .ToList();


            foreach (var export in exportsToSerialize)
            {
                FixObjectReferencesInFilteredExports(export, exportsToSerialize, package.Exports);
            }

            return exportsToSerialize;
        }

        private void FixObjectReferencesInFilteredExports(DummyExportTableItem export, List<DummyExportTableItem> exportsToSerialize, List<UExportTableItem> exports)
        {
            export.newClassIndex = FindNewExportReference(export.original.ClassIndex, exportsToSerialize, exports);
            export.newSuperIndex = FindNewExportReference(export.original.SuperIndex, exportsToSerialize, exports);
            export.newOuterIndex = FindNewExportReference(export.original.OuterIndex, exportsToSerialize, exports);
            export.newArchetypeIndex = FindNewExportReference(export.original.ArchetypeIndex, exportsToSerialize, exports);
        }

        private int FindNewExportReference(int originalIndex, List<DummyExportTableItem> exportsToSerialize, List<UExportTableItem> exports)
        {
            if (originalIndex <= 0)
            {
                return originalIndex;
            }
            var reference = exports[originalIndex - 1];
            var newReferenceIndex = exportsToSerialize.FindIndex((e) => e.original == reference) + 1;
            return newReferenceIndex;
        }

        private void SerializeImportsTable()
        {
            var importOffset = UW.BaseStream.Position;
            WriteIntAtPosition(package.Imports.Count(), ImportCountPosition);
            WriteIntAtPosition((int)importOffset, ImportCountPosition + 4);
            foreach (var import in package.Imports)
            {
                import.Serialize(this);
            }
        }

        private void SerializeNameTable()
        {
            var nameOffset = UW.BaseStream.Position;
            WriteIntAtPosition(package.Names.Count(), NameCountPosition);
            WriteIntAtPosition((int)nameOffset, NameCountPosition + 4);

            foreach (var name in package.Names)
            {
                name.Serialize(this);
            }
        }

        private void WriteIntAtPosition(int value, int writePosition)
        {
            var oldPosition = UW.BaseStream.Position;
            UW.BaseStream.Position = writePosition;
            UW.Write(value);
            UW.BaseStream.Position = oldPosition;
        }

        private void initHeader()
        {
            UW.BaseStream.Position = 0;
            //Tag
            UW.Write(UnrealPackage.Signature);
            //File Version
            var version = (int)(DummyFileVersion & 0x0000FFFFU) | DummyLicenseeVersion << 16;
            UW.Write(version);
            //Total HeaderSize will be calculated later
            UW.Write(0);
            //Group name
            UW.WriteString("None");
            //Package flags
            UW.Write(DummyPackageFlag);

            //Name / Import / Export count and offsets
            UW.Write(0);
            UW.Write(1);
            UW.Write(2);
            UW.Write(3);
            UW.Write(4);
            UW.Write(5);

            //DependsOffset / ImportExportGuidsOffset / ImportGuidsCount / ExportGuidsCount / ThumbnailTableOffset
            UW.Write(0);
            UW.Write(0);
            UW.Write(0);
            UW.Write(0);
            UW.Write(0);

            //guid (this might work?..)
            UW.Write(Guid.Parse(package.GUID).ToByteArray(), 0, 16);

            //Generations
            package.Generations.Serialize(this);

            //EngineVersion
            UW.Write(DummyEngineVersion);

            //CookerVersion
            UW.Write(DummyCookerVersion);
            //CompressionFlags
            UW.Write(DummyCompression);
            //compressedChunks
            UW.Write(DummyCompressedChunksData);
            //Unknown5
            UW.Write(DummyUnknown5);
            //UnknownStringArray
            UW.Write(DummyUnknownStringArray);
            //UnknownTypeArray
            UW.Write(DummyUnknownTypeArray);
        }

        private const int DummySerialSize = 12;

        private void DummyExportTableSerialize(DummyExportTableItem tableItem, int serialOffset, int serialSize)
        {
            this.Write(tableItem.newClassIndex);
            this.Write(tableItem.newSuperIndex);
            this.Write(tableItem.newOuterIndex);
            this.Write(tableItem.original.ObjectName);
            this.Write(tableItem.newArchetypeIndex);
            //this.Write(tableItem.ObjectFlags);
            if (tableItem.original.ClassName == "Package")
            {
                this.Write(1970342016843776);
            }
            else
            {
                this.Write(4222141830530048);
            }

            this.Write(serialSize);

            //this.Write(tableItem.SerialOffset);
            this.Write(serialOffset);
            //this.Write(tableItem.ExportFlags);
            this.Write(0);
            //Net objects count
            this.Write(0);

            //PackageGUID
            this.Write(0);
            this.Write(0);
            this.Write(0);
            this.Write(0);

            //Package flags
            if (tableItem.original.ClassName == "Package")
            {
                this.Write(1);
            }
            else
            {
                this.Write(0);
            }
        }
    }
}