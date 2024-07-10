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
        public abstract string OutOfStockBadgeSelector { get; }
        public abstract string NextPageLinkSelector { get; }
        public abstract string ItemsSelector { get; }
        public abstract string ItemLinkSelector { get; }
        public abstract string ItemTitleSelector { get; }
        public abstract string ItemVariationsSelector { get; }
        public abstract string ProductDetailsSelector { get; }
        public abstract string VariationsSelector { get; }
        public abstract string DropListSelector { get; }
        public abstract string ComboDropListSelector { get; }
        public abstract string DropListVariationsSelector { get; }
        public abstract string ComboDropListVariationsSelector { get; }
        public abstract string VariationOutOfStockLabel_1 {get;}
        public abstract string VariationOutOfStockLabel_2 { get; }
        public abstract string AssemblyOptionSelector { get; }

    }
    public class DssCatalogSelectors : CatalogSelectors
    {
        public override string OutOfStockBadgeSelector => "body > main > div.container.main__inner-wrapper > div.row.product-details > div.col-sm-6.product-details__left > div > div.js-zoom-target > div.purchase > div.qty-limited";
        public override string ProductGridSelector => "div.product-grid";
        public override string NextPageDisabledSelector => "main div.product-grid div.grid-content div.pagination-bar.top div.pagination-wrap.hidden-sm.hidden-xs ul > li.pagination-next.disabled.img-link";
        public override string NextPageLinkSelector => "main div.product-grid div.grid-content div.pagination-bar.top div.pagination-wrap.hidden-sm.hidden-xs ul > li.pagination-next.img-link > a";
        public override string ItemsSelector => "body > main > div.container.main__inner-wrapper > div.product-grid > div.grid-content > div > ul > div.product-item";
        public override string ItemLinkSelector => "a";
        public override string ItemTitleSelector => "div.details-outer > div > div.details-inner > a > span";
        public override string ItemVariationsSelector => "div.details-outer > div > div.colorswatch";
        public override string ProductDetailsSelector => "div.product-details";
        public override string VariationsSelector => "body > main > div.container.main__inner-wrapper > div.row.product-details > div.col-sm-6.product-details__left > div > div.js-zoom-target > div.purchase > div.product-option > ul > li";
        public override string DropListSelector => "#priority1";
        public override string ComboDropListSelector => "#priority2";
        public override string VariationOutOfStockLabel_1 => "body > main > div.container.main__inner-wrapper > div.row.product-details > div.col-sm-6.product-details__left > div > div.js-zoom-target > div.purchase > div.qty-limited";
        public override string VariationOutOfStockLabel_2 => "body > main > div.container.main__inner-wrapper > div.row.product-details > div.col-sm-6.product-image_container > div > div.carousel-main > div > div.badges > div";
        public override string DropListVariationsSelector => "#priority1 > option";
        public override string AssemblyOptionSelector => "body > main > div.container.main__inner-wrapper > div.row.product-details > div.col-sm-6.product-details__left > div > div.js-zoom-target > div.purchase > div.product-option > a";
        public override string ComboDropListVariationsSelector => "#priority2 > option";
    }
    public class RgsCatalogSelectors : CatalogSelectors
    {
        public override string OutOfStockBadgeSelector => throw new NotImplementedException();
        public override string ProductGridSelector => "body > main > div.main__inner-wrapper.container-fluid > div.product-grid > div.grid-content > div > ul";

        public override string NextPageDisabledSelector => "body > main > div.main__inner-wrapper.container-fluid > div.product-grid > div.grid-content > div > div.pagination-bar.top > div > div > div.pagination-wrap.hidden-sm.hidden-xs > ul > li.pagination-next.disabled.img-link";

        public override string NextPageLinkSelector => "body > main > div.main__inner-wrapper.container-fluid > div.product-grid > div.grid-content > div > div.pagination-bar.top > div > div > div.pagination-wrap.hidden-sm.hidden-xs > ul > li.pagination-next.img-link > a";

        public override string ItemsSelector => "body > main > div.main__inner-wrapper.container-fluid > div.product-grid > div.grid-content > div > ul > div.product-item";

        public override string ItemLinkSelector => "a";

        public override string ItemTitleSelector => "a.thumb";


        public override string ItemVariationsSelector => "div.details-outer > div > div.colorswatch";

        public override string ProductDetailsSelector => "body > main > div.main__inner-wrapper.container-fluid > div.container.pdp-product-main > div.row.pdp-product-main-wrap > div.col-xs-12.col-sm-6.col-md-6.col-lg-5.right-content > div";

        public override string VariationsSelector => "#priority1";
        public override string DropListSelector => "#priority1";
        public override string ComboDropListSelector => "#priority2";
        public override string VariationOutOfStockLabel_1 => "body > main > div.container.main__inner-wrapper > div.row.product-details > div.col-sm-6.product-details__left > div > div.js-zoom-target > div.purchase > div.qty-limited";

        public override string VariationOutOfStockLabel_2 => "body > main > div.container.main__inner-wrapper > div.row.product-details > div.col-sm-6.product-image_container > div > div.carousel-main > div > div.badges > div";
        public override string DropListVariationsSelector => "#priority1 > option";
        public override string AssemblyOptionSelector => "body > main > div.container.main__inner-wrapper > div.row.product-details > div.col-sm-6.product-details__left > div > div.js-zoom-target > div.purchase > div.product-option > a";
        public override string ComboDropListVariationsSelector => "#priority2 > option";
    }

}
