//! USB ID Repository lookup. IDs describe reported hardware, not verified brands.
const USB_IDS: &[u8] = include_bytes!("../../../third_party/usb-ids/usb.ids");

/// The registered USB vendor and, when present in the list, its product.
pub fn lookup(vendor: u16, product: u16) -> (Option<&'static str>, Option<&'static str>) {
    lookup_in(USB_IDS, vendor, product)
}

fn lookup_in(data: &[u8], vendor: u16, product: u16) -> (Option<&str>, Option<&str>) {
    let mut found = None;
    for bytes in data.split(|b| *b == b'\n') {
        if bytes.is_empty() || bytes.starts_with(b"#") || bytes.starts_with(b"\t\t") {
            continue;
        }
        // A malformed name must not let a following vendor's products match.
        if found.is_some() && !bytes.starts_with(b"\t") {
            break;
        }
        let Ok(line) = std::str::from_utf8(bytes) else {
            continue;
        };
        let is_product = line.starts_with('\t');
        let line = line.trim_start_matches('\t');
        let Some((id, name)) = line.split_once("  ") else {
            continue;
        };
        if id.len() != 4 {
            continue;
        }
        let Ok(id) = u16::from_str_radix(id, 16) else {
            continue;
        };
        if is_product {
            if found.is_some() && id == product {
                return (found, Some(name.trim()));
            }
        } else if found.is_some() {
            break;
        } else if id == vendor {
            found = Some(name.trim());
        }
    }
    (found, None)
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn vendor_product_and_unknown_are_scoped_correctly() {
        let ids = "# header\n0001  First\n\t0002  First product\n\t\t0003  Interface\n0002  Second\n\t0002  Second product\n";
        assert_eq!(
            lookup_in(ids.as_bytes(), 1, 2),
            (Some("First"), Some("First product"))
        );
        assert_eq!(lookup_in(ids.as_bytes(), 1, 3), (Some("First"), None));
        assert_eq!(
            lookup_in(ids.as_bytes(), 2, 2),
            (Some("Second"), Some("Second product"))
        );
        assert_eq!(lookup_in(ids.as_bytes(), 3, 2), (None, None));
    }
    #[test]
    fn invalid_vendor_encoding_does_not_cross_vendor_boundaries() {
        let ids = b"0001  First\n0002  Invalid \xff\n\t0003  Other product\n";
        assert_eq!(lookup_in(ids, 1, 3), (Some("First"), None));
    }
    #[test]
    fn apple_vendor_is_a_reported_id_not_a_brand_claim() {
        assert_eq!(lookup(0x05ac, 0x3b06).0, Some("Apple, Inc."));
    }
}
