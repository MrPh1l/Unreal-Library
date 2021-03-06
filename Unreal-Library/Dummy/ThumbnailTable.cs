﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UELib.Dummy
{
    class ThumbnailTable
    {

        List<ThumbnailTableItem> thumbnailTable;
        List<ThumbnailDataItem> thumbnailDataTable;

        public int thumbnailDataOffset;
        public int thumbnailTableOffset;

        public void Init(List<DummyExportTableItem> dummyExports)
        {
            thumbnailTable = new List<ThumbnailTableItem>();
            thumbnailDataTable = new List<ThumbnailDataItem>();
            var exportsWithThumbnail = dummyExports.Where((e) => e.packageFlag == 0).ToList();
            foreach (var export in exportsWithThumbnail)
            {
                thumbnailTable.Add(new ThumbnailTableItem(export.original.ObjectName, export.original.OuterName, 0));
                // Can everything be a 0 pixel large thumbnail? 
                thumbnailDataTable.Add(new ThumbnailDataItem(0, 0, null));
            }
        }

        public int GetSerialSize()
        {
            int sum = 0;
            foreach (var item in thumbnailDataTable)
            {
                sum += item.GetSerialSize();
            }

            sum += sizeof(int);
            foreach(var item in thumbnailTable)
            {
                sum += item.GetSerialSize();
            }
            return sum;
        }

        public void Serialize(IUnrealStream stream)
        {
            var offsetList = new List<int>();
            thumbnailDataOffset = (int)stream.Position;
            foreach (var thumbnailData in thumbnailDataTable)
            {
                offsetList.Add((int)stream.Position);
                thumbnailData.Serialize(stream);
            }

            thumbnailTableOffset = (int)stream.Position;
            stream.Write(thumbnailTable.Count());
            for (int i = 0; i < thumbnailTable.Count(); i++)
            {
                thumbnailTable[i].dataOffset = offsetList[i];
                thumbnailTable[i].Serialize(stream);
            }

        }
    }

    class ThumbnailTableItem
    {
        string name;
        string group;
        public int dataOffset;

        public ThumbnailTableItem(string name, string group, int dataOffset)
        {
            this.name = name;
            this.group = group;
            this.dataOffset = dataOffset;
        }

        public int GetSerialSize() => name.Length
                                   + 1 //null termination
                                   + group.Length
                                   + 1 //null termination
                                   + sizeof(int)* 3; //name length, group length, dataOffset

        internal void Serialize(IUnrealStream stream)
        {
            stream.WriteString(name);
            stream.WriteString(group);
            stream.Write(dataOffset);
        }
    }

    class ThumbnailDataItem
    {
        public int sizeX;
        public int sizeY;
        public int dataSize;
        public byte[] data;

        public ThumbnailDataItem(int sizeX, int sizeY, byte[] data)
        {
            this.sizeX = sizeX;
            this.sizeY = sizeY;
            this.data = data;
            if (data == null)
            {
                dataSize = 0;
            }else
            {
                dataSize = data.Length;
            }
        }

        public int GetSerialSize() => dataSize + 3 * sizeof(int);

        internal void Serialize(IUnrealStream stream)
        {
            stream.Write(sizeX);
            stream.Write(sizeY);
            stream.Write(dataSize);
            if (dataSize > 0)
            {
                stream.Write(data, 0, data.Length);
            }
        }
    }
}
