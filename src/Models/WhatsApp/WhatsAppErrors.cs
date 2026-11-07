namespace Models.WhatsApp
{
    /// <summary>
    /// Os códigos de erro do envio (página oficial de códigos de erro do Cloud API) em português, para a tela, o PDF
    /// do gestor e o registro. O mesmo texto vale para a recusa na hora do envio e para a falha que chega no aviso.
    /// </summary>
    public static class WhatsAppErrors
    {
        public static string Describe(int? iCode, string sFallback) => iCode switch
        {
            0 or 190 => "Token inválido ou vencido: o WhatsApp parou. Troque o token em Configurações → WhatsApp.",
            3 or 10 or 200 or 131005 => "O token não tem permissão para enviar mensagens por este número.",
            368 or 131031 => "A conta do WhatsApp da empresa está restrita pela Meta.",
            131042 => "Problema na forma de pagamento da conta na Meta.",
            131045 or 133010 => "O número da empresa não está registrado na API do WhatsApp.",
            131037 => "O número da empresa ainda não tem o nome aprovado pela Meta.",
            131048 => "A Meta limitou os envios deste número por bloqueios e denúncias (veja a qualidade no WhatsApp Manager).",
            131064 => "A Meta atingiu o limite de envios deste número.",
            132001 => "O texto aprovado não foi encontrado na Meta (apagado ou não aprovado).",
            132015 => "A Meta pausou o texto desta campanha por baixa qualidade.",
            132016 => "A Meta desativou o texto desta campanha.",
            132000 or 132005 or 132007 or 132012 => "A Meta recusou os campos do texto: " + sFallback,
            131026 => "Este número não tem WhatsApp (ou não recebe mensagens de empresas).",
            131050 => "O cliente desligou as promoções da empresa no WhatsApp.",
            131049 => "A Meta segurou a mensagem para não mandar promoções demais a este cliente.",
            130403 => "O cliente bloqueou a empresa no WhatsApp.",
            131021 => "O número do cliente é o mesmo da empresa.",
            131056 => "Mensagens demais para este cliente em pouco tempo.",
            131000 => "A Meta não conseguiu entregar (erro desconhecido dela).",
            _ => sFallback.Length > 0 ? sFallback : $"A Meta não entregou (código {iCode}).",
        };
    }
}
