# Gişe İşlem Rehberi

## Roller ve yetkiler

Sistemde beş rol vardır. Her personel yalnızca rolünün izin verdiği işlemleri yapabilir.

**Yönetici (Administrator):** Her işlemi yapabilir. Maç iptali, ciro raporu, kullanıcı ve rol yönetimi yalnızca yöneticide vardır.

**Organizatör (MatchManager):** Salon ve stadyum tanımlar, spor kategorisi tanımlar, yeni maç açar. Bilet satamaz, koltuk tutamaz, maç iptal edemez.

**Gişe (BoxOffice):** Maçları görür, müşteri adına koltuk tutar ve bilet satar. Sistemdeki herkesin biletini görüntüleyebilir. Maç açamaz, maç iptal edemez, iade başlatamaz.

**Müşteri (Customer):** Maçları görür, kendisi için koltuk tutar ve bilet alır, yalnızca kendi biletlerini görür.

**İzleyici (Viewer):** Maçları ve biletleri yalnızca görüntüler, hiçbir işlem yapamaz.

## Gişeden bilet satışı

Gişe personeli, gişeye gelen müşteri için koltuk tutup satın alma işlemini yapabilir; yani başkası adına bilet almak gişede mümkündür. İşlem gişe personelinin kendi oturumuyla yapılır; ayrı bir "müşteri adına" ekranı yoktur.

Müşterilerin kendileri ise yalnızca kendi hesaplarından bilet alır; bir müşteri sistem üzerinden başka bir kişi adına bilet alamaz.

Gişeden yapılan satışta da koltuk tutma süresi 10 dakikadır ve ödeme kartla alınır. Nakit ödeme sistemde yoktur.

## Bilet sorgulama

Gişe personeli bir biletin numarasıyla bileti sorgulayabilir ve biletin hangi maça, hangi koltuğa ait olduğunu ve durumunu görebilir. Bilet durumu "kesildi" ya da "iptal edildi" olur.

Müşteri kendi biletlerini kendi hesabından görür; gişe ise herkesin biletini görebilir.

## Maç iptali

Maç iptalini yalnızca yönetici yapabilir. Gişe personeline maç iptali için başvuran biri yöneticiye yönlendirilir.

Bir maç yalnızca başlama saatinden önce iptal edilebilir. Başlamış bir maç iptal edilemez.

İptal sırasında bir gerekçe girilir; bu gerekçe müşterilere giden e-postada yer alır.

## Ciro raporu

Bir maçın cirosunu yalnızca yönetici görebilir. Gişe ve organizatör ciro raporuna erişemez.

## Yeni maç açma

Yeni maç açmak organizatörün işidir. Maçın başlama saati gelecekte olmalıdır; geçmiş bir tarihe maç açılamaz. Maç açıldığında koltuklar salonun blok düzenine göre otomatik oluşturulur ve maç hemen satışa çıkar.
