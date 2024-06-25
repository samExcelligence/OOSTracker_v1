using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OOSWebScraper.models
{
    public class OOSItemDetails
    {
        public string ItemName { get; set; }
        public string UPID { get; set; }
        public string StockStatus { get; set; }
        public DateTime RetrievedAt { get; set; }
        public int PageNumber { get; set; }
        public int PositionOnPage { get; set; }
        public bool HasVariations { get; set; }
        public string ItemURL { get; set; }
        public List<Variation> Variations { get; set; }
    }

    public class Variation
    {
        public string Name { get; set; }
        public bool IsOutOfStock { get; set; }
        public string UPID { get; set; }
        public string StockStatus { get; set; }
    }
}
