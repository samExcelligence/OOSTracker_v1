using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OOSWebScrapper.Models
{
    public abstract class CatalogSelectors
    {
        public abstract string ProductGridSelector { get; }
        public abstract string NextPageDisabledSelector { get; }
        public abstract string NextPageLinkSelector { get; }
        public abstract string ItemsSelector { get; }
        public abstract string ItemLinkSelector { get; }
        public abstract string ItemTitleSelector { get; }
        public abstract string ItemVariationsSelector { get; }
        public abstract string ProductDetailsSelector { get; }
        public abstract string VariationsSelector { get; }
    }
    public class DssOutOfStockSelectors : CatalogSelectors
    {
        public override string ProductGridSelector => "div.product-grid";
        public override string NextPageDisabledSelector => "main div.product-grid div.grid-content div.pagination-bar.top div.pagination-wrap.hidden-sm.hidden-xs ul > li.pagination-next.disabled.img-link";
        public override string NextPageLinkSelector => "main div.product-grid div.grid-content div.pagination-bar.top div.pagination-wrap.hidden-sm.hidden-xs ul > li.pagination-next.img-link > a";
        public override string ItemsSelector => "body > main > div.container.main__inner-wrapper > div.product-grid > div.grid-content > div > ul > div.product-item";
        public override string ItemLinkSelector => "a";
        public override string ItemTitleSelector => "div.details-outer > div > div.details-inner > a > span";
        public override string ItemVariationsSelector => "div.details-outer > div > div.colorswatch";
        public override string ProductDetailsSelector => "div.product-details";
        public override string VariationsSelector => "body > main > div.container.main__inner-wrapper > div.row.product-details > div.col-sm-6.product-details__left > div > div.js-zoom-target > div.purchase > div.product-option > ul > li";
    }
    public class RgsOutOfStockSelectors : CatalogSelectors
    {
        // Add the specific selectors for RGS Out of Stock Catalog here
        public override string ProductGridSelector => "specific RGS product grid selector";
        public override string NextPageDisabledSelector => "specific RGS next page disabled selector";
        public override string NextPageLinkSelector => "specific RGS next page link selector";
        public override string ItemsSelector => "specific RGS items selector";
        public override string ItemLinkSelector => "specific RGS item link selector";
        public override string ItemTitleSelector => "specific RGS item title selector";
        public override string ItemVariationsSelector => "specific RGS item variations selector";
        public override string ProductDetailsSelector => "specific RGS product details selector";
        public override string VariationsSelector => "specific RGS variations selector";
    }

    public class RgsComingSoonSelectors : CatalogSelectors
    {
        // Add the specific selectors for RGS Coming Soon Catalog here
        public override string ProductGridSelector => "specific RGS Coming Soon product grid selector";
        public override string NextPageDisabledSelector => "specific RGS Coming Soon next page disabled selector";
        public override string NextPageLinkSelector => "specific RGS Coming Soon next page link selector";
        public override string ItemsSelector => "specific RGS Coming Soon items selector";
        public override string ItemLinkSelector => "specific RGS Coming Soon item link selector";
        public override string ItemTitleSelector => "specific RGS Coming Soon item title selector";
        public override string ItemVariationsSelector => "specific RGS Coming Soon item variations selector";
        public override string ProductDetailsSelector => "specific RGS Coming Soon product details selector";
        public override string VariationsSelector => "specific RGS Coming Soon variations selector";
    }


}
